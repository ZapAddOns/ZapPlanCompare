using NLog;
using ScottPlot.Plottable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ZapClient.Data;
using ZapPlanCompare.Extensions;
using ZapSurgical.Data;
using ZapTranslation;

namespace ZapPlanCompare
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly NLog.Logger _logger = LogManager.GetCurrentClassLogger();
        private ZapClient.ZapClient _client;

        const char lineTL = '┌';
        const char lineTC = '┬';
        const char lineTR = '┐';
        const char lineBL = '└';
        const char lineBC = '┴';
        const char lineBR = '┘';
        const char lineL = '├';
        const char lineR = '┤';
        const char lineC = '┼';
        const char lineV = '│';
        const char lineH = '─';

        const int widthC1 = 3;
        const int widthC2 = 26;
        const int widthC3 = 15;
        const int widthC1_C2 = widthC1 + 1 + widthC2;
        const int widthC2_C3 = widthC2 + 1 + widthC3;
        const int widthC1_C2_C3 = widthC1_C2 + 1 + widthC3;
        const int widthIndices = 11;
        const int widthDetails = 15;
        const int widthPlan = widthDetails * 3 + 2;

        readonly SolidColorBrush _colorGreen = new SolidColorBrush(Colors.Green);
        readonly SolidColorBrush _colorRed = new SolidColorBrush(Colors.Red);
        readonly SolidColorBrush _colorSelectedForeground = new SolidColorBrush(Colors.White);
        readonly SolidColorBrush _colorSelectedBackground = new SolidColorBrush(Colors.LightBlue);

        Paragraph _paragraphHeader = new Paragraph();
        Paragraph _paragraphIndices = new Paragraph();
        Paragraph _paragraphStructures = new Paragraph();

        CompareConfig _config;

        List<Patient> _patientList;
        List<Plan> _planList;

        Plan _selectedPlan;
        VOIContour _selectedStructure;

        Dictionary<string, Plan> _plans = new Dictionary<string, Plan>();
        Dictionary<string, Plan> _plansToCompare = new Dictionary<string, Plan>();
        Dictionary<Plan, PlanData> _planData = new Dictionary<Plan, PlanData>();
        Dictionary<Plan, ZapClient.Data.PlanSummary> _planSummary = new Dictionary<Plan, ZapClient.Data.PlanSummary>();
        Dictionary<Plan, BeamData> _planBeamData = new Dictionary<Plan, BeamData>();
        Dictionary<Plan, VOIData> _planVOIs = new Dictionary<Plan, VOIData>();
        Dictionary<Plan, DoseVolumeData> _planDVData = new Dictionary<Plan, DoseVolumeData>();
        Dictionary<string, VOIContour> _listOfStructures = new Dictionary<string, VOIContour>();
        Dictionary<VOIContour, ScatterPlot> _listOfPlots = new Dictionary<VOIContour, ScatterPlot>();

        MarkerPlot _highlightedPoint;
        int _lastHighlightedIndex = -1;
        //HLine _plotLineHori;
        VLine _plotLineVert;

        DpiScale _dpiInfo;
        readonly object _sync = new object();

        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += Init;
        }

        private void Init(object sender, RoutedEventArgs e)
        {
            // Add a red circle we can move around later as a highlighted point indicator
            _highlightedPoint = plot.Plot.AddPoint(0, 0);
            _highlightedPoint.Color = System.Drawing.Color.Red;
            _highlightedPoint.MarkerSize = 10;
            _highlightedPoint.MarkerShape = ScottPlot.MarkerShape.openCircle;
            _highlightedPoint.IsVisible = false;

            plot.Plot.Style(figureBackground: ((SolidColorBrush)this.Background).Color.ToColor());
            plot.Plot.Style(titleLabel: Colors.White.ToColor());
            plot.Plot.Style(axisLabel: Colors.White.ToColor());
            plot.Plot.Style(tick: Colors.White.ToColor());

            _plotLineVert = plot.Plot.AddVerticalLine(0);
            _plotLineVert.LineWidth = 2;
            _plotLineVert.PositionLabel = true;
            _plotLineVert.PositionLabelBackground = _plotLineVert.Color;
            _plotLineVert.IsVisible = false;

            CreateConfig();
            CreateLogger();

            // Set default language
            if (!string.IsNullOrEmpty(_config.Culture))
            {
                System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(_config.Culture);
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(_config.Culture);
            }

            // Localization
            Title = Translate.GetString("PlanCompare");
            lblPatient.Content = Translate.GetString("Patient");
            lblPlans.Content = Translate.GetString("Plans");
            btnRefresh.Content = Translate.GetString("Refresh");

            try
            {
                lock (_sync)
                    _dpiInfo = VisualTreeHelper.GetDpi(this);

                cbPatient.SelectionChanged += cbPatient_SelectionChanged;
                rtOutput.MouseDoubleClick += RtOutput_MouseLeftButtonUp;
                plot.MouseMove += plot_MouseMove;

                _client = new ZapClient.ZapClient(GetUsernameAndPassword, _logger);

                if (!_client.OpenConnection())
                {
                    Close();
                    return;
                }

                UpdatePatients();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }

            this.Loaded -= Init;

            cbPatient.Focus();
        }

        private void plot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // determine point nearest the cursor
            if (_selectedStructure == null || _selectedPlan == null || !_listOfPlots.ContainsKey(_selectedStructure))
                return;

            var scatterPlotOfStructure = _listOfPlots[_selectedStructure];

            (double mouseCoordX, _) = plot.GetMouseCoordinates();
            (double pointX, double pointY, int pointIndex) = scatterPlotOfStructure.GetPointNearestX(mouseCoordX);

            // place the highlight over the point of interest
            _highlightedPoint.X = pointX;
            _highlightedPoint.Y = pointY;
            _highlightedPoint.IsVisible = true;

            var dvData = _planDVData[_selectedPlan].DVData.Where((s) => s.VOIUUID == _selectedStructure.UUID).First();

            if (dvData == null)
                return;

            // render if the highlighted point chnaged
            if (_lastHighlightedIndex != pointIndex)
            {
                _lastHighlightedIndex = pointIndex;

                Func<double, string> xFormatter = x => $"Dose: {pointX:0.0} cGy ({dvData.DVHDosePercentValues[pointIndex] * 100.0:0.0} %)\nVolume: {pointY * 100.0:0.000} % ({dvData.DVHVolumeValues[pointIndex]:0.000} mm³)";
                _plotLineVert.PositionFormatter = xFormatter;
                _plotLineVert.X = pointX;
                _plotLineVert.IsVisible = true;

                plot.Render();
            }
        }

        private void RtOutput_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var run = (Run)rtOutput.GetPositionFromPoint(e.GetPosition(rtOutput), true).Parent;
            var text = run.Text.Trim();

            if (_paragraphHeader.Inlines.Contains(run))
            {
                var pos = -1;
                var runs = _paragraphHeader.Inlines.ToArray();
                for (var i = 0; i < _paragraphHeader.Inlines.Count; i++)
                {
                    if (runs[i] == run)
                        pos = (i - 4) / 2;
                }

                if (pos >= 0)
                {
                    _logger.Info($"User clicked on plan {text}, which is run {pos}");

                    _selectedPlan =  _plansToCompare.ElementAt(pos).Value;

                    e.Handled = true;

                    CreateHeader();
                    UpdateDocument();
                    UpdateGraphics();
                }
            }

            if (_paragraphStructures.Inlines.Contains(run))
            {
                var pos = -1;
                var runs = _paragraphStructures.Inlines.ToArray();
                for (var i = 0; i < _paragraphStructures.Inlines.Count; i++)
                {
                    if (runs[i] == run)
                    {
                        pos = (int)((i - 6) / (8 + 6 * _plansToCompare.Count));

                        _logger.Info($"User clicked on structure '{text}', which is run {i} or pos {pos}");
                    }
                }

                if (pos >= 0)
                {
                    _selectedStructure = _listOfStructures.OrderBy((s) => s.Value == null ? "zzzz" : (9 - (int)s.Value.Type).ToString("0") + s.Value.Name).ElementAt(pos).Value;

                    CreateStructures();
                    UpdateDocument();
                    UpdateGraphics();
                }
            }
        }

        private void cbArchived_Click(object sender, RoutedEventArgs e)
        {
            UpdatePatients();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _client?.CloseConnection();
            _client?.OpenConnection();

            UpdatePatients();
        }

        private void cbPatient_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (cbPatient.SelectedItem is null)
                return;

            var text = cbPatient.SelectedItem.ToString();
            var medicalId = text.Substring(0, text.IndexOf(" - "));

            try
            {
                UpdatePlansForPatient(medicalId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private FlowDocument CreateDocument()
        {
            FlowDocument doc = new FlowDocument();

            if (_plansToCompare.Count > 0)
            {
                doc.Blocks.Add(_paragraphHeader);
                doc.Blocks.Add(_paragraphIndices);
                doc.Blocks.Add(_paragraphStructures);
            }

            // Get size of table
            var size = MeasureString(new String('m', widthC1_C2_C3 + 2 + _plansToCompare.Count * (widthPlan + 1) + 2));

            doc.PageWidth = size.Width;

            return doc;
        }

        private void CreateHeader()
        {
            var paragraph = new Paragraph();
            var list = new List<string>();
            var sb = new StringBuilder();
            var runsPrescribedDose = new List<Run>();
            var runsPrescribedIsodose = new List<Run>();
            var runsFractions = new List<Run>();
            var runsTotalMUs = new List<Run>();
            var runsNumOfIsocenters = new List<Run>();
            var runsNumOfBeams = new List<Run>();
            var runsTreatmentTime = new List<Run>();
            var runsPlannames = new List<Run>();

            runsPrescribedDose.Add(new Run(lineV.ToString()));
            runsPrescribedDose.Add(new Run(Translate.GetString("PrescribedDose").LeftAlign(widthC1_C2_C3)));
            runsPrescribedDose.Add(new Run(lineV.ToString()));
            var s = lineV + Translate.GetString("PrescribedDose").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsPrescribedDose.Add(new Run(planSummary.PrescribedDose.ToString("#,#0").CenterAlign(widthPlan)));
                runsPrescribedDose.Add(new Run(lineV.ToString()));
                s += planSummary.PrescribedDose.ToString("#,#0").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsPrescribedIsodose.Add(new Run(lineV.ToString()));
            runsPrescribedIsodose.Add(new Run(Translate.GetString("PrescribedIsodose").LeftAlign(widthC1_C2_C3)));
            runsPrescribedIsodose.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("PrescribedIsodose").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsPrescribedIsodose.Add(new Run((planSummary.PrescribedPercent / 100.0).ToString("0.0 %").CenterAlign(widthPlan)));
                runsPrescribedIsodose.Add(new Run(lineV.ToString()));
                s += (planSummary.PrescribedPercent / 100.0).ToString("0.0 %").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsTotalMUs.Add(new Run(lineV.ToString()));
            runsTotalMUs.Add(new Run(Translate.GetString("TotalMU").LeftAlign(widthC1_C2_C3)));
            runsTotalMUs.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("TotalMU").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsTotalMUs.Add(new Run(planSummary.TotalMUs.ToString("#,#0.00").CenterAlign(widthPlan)));
                runsTotalMUs.Add(new Run(lineV.ToString()));
                s += planSummary.TotalMUs.ToString("#,#0.00").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsFractions.Add(new Run(lineV.ToString()));
            runsFractions.Add(new Run(Translate.GetString("NumOfFractions").LeftAlign(widthC1_C2_C3)));
            runsFractions.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("NumOfFractions").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsFractions.Add(new Run(planSummary.TotalFractions.ToString("0").CenterAlign(widthPlan)));
                runsFractions.Add(new Run(lineV.ToString()));
                s += planSummary.TotalFractions.ToString("0").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsNumOfIsocenters.Add(new Run(lineV.ToString()));
            runsNumOfIsocenters.Add(new Run(Translate.GetString("NumOfIsocenters").LeftAlign(widthC1_C2_C3)));
            runsNumOfIsocenters.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("NumOfIsocenters").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsNumOfIsocenters.Add(new Run(planSummary.TotalIsocenters.ToString("#,#0").CenterAlign(widthPlan)));
                runsNumOfIsocenters.Add(new Run(lineV.ToString()));
                s += planSummary.TotalIsocenters.ToString("#,#0").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsNumOfBeams.Add(new Run(lineV.ToString()));
            runsNumOfBeams.Add(new Run(Translate.GetString("NumOfBeams").LeftAlign(widthC1_C2_C3)));
            runsNumOfBeams.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("NumOfBeams").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsNumOfBeams.Add(new Run(planSummary.TotalBeamsWithMU.ToString("#,#0").CenterAlign(widthPlan)));
                runsNumOfBeams.Add(new Run(lineV.ToString()));
                s += planSummary.TotalBeamsWithMU.ToString("#,#0").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsTreatmentTime.Add(new Run(lineV.ToString()));
            runsTreatmentTime.Add(new Run(Translate.GetString("TreatmentTime").LeftAlign(widthC1_C2_C3)));
            runsTreatmentTime.Add(new Run(lineV.ToString()));
            s = lineV + Translate.GetString("TreatmentTime").LeftAlign(widthC1_C2_C3) + lineV;
            foreach (var plan in _plansToCompare)
            {
                var planSummary = _planSummary[plan.Value];
                runsTreatmentTime.Add(new Run(TimeSpan.FromSeconds(planSummary.TotalTreatmentTime).ToString("hh\\:mm\\:ss").CenterAlign(widthPlan)));
                runsTreatmentTime.Add(new Run(lineV.ToString()));
                s += TimeSpan.FromSeconds(planSummary.TotalTreatmentTime).ToString("hh\\:mm\\:ss").CenterAlign(widthPlan) + lineV;
            }
            list.Add(s);

            runsPlannames.Add(new Run(lineV.ToString()));
            runsPlannames.Add(new Run(Translate.GetString("Plannames").LeftAlign(widthC1_C2_C3)));
            runsPlannames.Add(new Run(lineV.ToString()));
            sb.Append(lineV + Translate.GetString("Plannames").LeftAlign(widthC1_C2_C3) + lineV);
            foreach (var plan in _plansToCompare)
            {
                var run = new Run(plan.Key.CenterAlign(widthPlan));
                if (plan.Value == _selectedPlan)
                {
                    run.Foreground = _colorSelectedForeground;
                    run.Background = _colorSelectedBackground;
                }
                runsPlannames.Add(run);
                runsPlannames.Add(new Run(lineV.ToString()));
                sb.Append(plan.Key.CenterAlign(widthPlan) + lineV);
            }

            string header = sb.ToString();

            paragraph.Inlines.Add(new Run(GetLine(null, header) + "\n"));
            foreach (var run in runsPlannames)
                paragraph.Inlines.Add(run);
            paragraph.Inlines.Add(new Run("\n"));
            paragraph.Inlines.Add(new Run(GetLine(header, list.First()) + "\n"));

            if (runsPrescribedDose.Count > 0)
            {
                foreach (var run in runsPrescribedDose)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsPrescribedIsodose.Count > 0)
            {
                foreach (var run in runsPrescribedIsodose)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsTotalMUs.Count > 0)
            {
                foreach (var run in runsTotalMUs)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsFractions.Count > 0)
            {
                foreach (var run in runsFractions)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsNumOfIsocenters.Count > 0)
            {
                foreach (var run in runsNumOfIsocenters)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsNumOfBeams.Count > 0)
            {
                foreach (var run in runsNumOfBeams)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            if (runsTreatmentTime.Count > 0)
            {
                foreach (var run in runsTreatmentTime)
                    paragraph.Inlines.Add(run);
                paragraph.Inlines.Add(new Run("\n"));
            }

            paragraph.Inlines.Add(new Run(GetLine(list.Last(), null)));

            _paragraphHeader = paragraph;
        }

        private void CreateIndices()
        {
            var paragraph = new Paragraph();
            var list = new List<string>();
            var sb = new StringBuilder();
            var runs = new List<Run>();

            foreach (var structure in _listOfStructures.Where(kv => kv.Value?.Type == VOIContourType.Target))
            {
                // Calc min and max
                var lowestCoverage = double.MaxValue;
                var highestCoverage = double.MinValue;
                var lowestCI = double.MaxValue;
                var highestCI = double.MinValue;
                var lowestNCI = double.MaxValue;
                var highestNCI = double.MinValue;
                var lowestGI = double.MaxValue;
                var highestGI = double.MinValue;

                Plan planToUse = null;
                foreach (var plan in _plansToCompare)
                {
                    var planDVData = _planDVData[plan.Value];

                    if (planDVData.DVData == null || planDVData.DVData.Count() == 0)
                        continue;

                    var v = planDVData.DVData.Where(sd => sd.VOIUUID == structure.Key);

                    if (v.Count() > 0)
                    {
                        planToUse = plan.Value;
                        lowestCoverage = Math.Min(lowestCoverage, v.First().Coverage);
                        highestCoverage = Math.Max(highestCoverage, v.First().Coverage);
                        lowestCI = Math.Min(lowestCI, v.First().CI);
                        highestCI = Math.Max(highestCI, v.First().CI);
                        lowestNCI = Math.Min(lowestNCI, v.First().nCI);
                        highestNCI = Math.Max(highestNCI, v.First().nCI);
                        lowestGI = Math.Min(lowestGI, v.First().GI);
                        highestGI = Math.Max(highestGI, v.First().GI);
                    }
                }

                // Print
                runs.Add(new Run(lineV.ToString()));
                runs.Add(new Run("T".CenterAlign(widthC1)));
                runs.Add(new Run(lineV.ToString()));
                runs.Add(new Run(_listOfStructures[structure.Key].Name.LeftAlign(widthC2_C3)));
                runs.Add(new Run(lineV.ToString()));
                sb.Append(lineV + "T".CenterAlign(widthC1) + lineV + _listOfStructures[structure.Key].Name.LeftAlign(widthC2_C3) + lineV);
                foreach (var plan in _plansToCompare)
                {
                    var planDDVData = _planDVData[plan.Value];
                    var s = planDDVData.DVData?.Where((sd) => sd.VOIUUID == structure.Key);

                    if (s == null || s.Count() == 0)
                    {
                        runs.Add(new Run("".RightAlign(widthIndices)));
                        runs.Add(new Run(lineV.ToString()));
                        runs.Add(new Run("".RightAlign(widthIndices)));
                        runs.Add(new Run(lineV.ToString()));
                        runs.Add(new Run("".RightAlign(widthIndices)));
                        runs.Add(new Run(lineV.ToString()));
                        runs.Add(new Run("".RightAlign(widthIndices)));
                        runs.Add(new Run(lineV.ToString()));
                        sb.Append("".RightAlign(widthIndices) + lineV);
                        sb.Append("".RightAlign(widthIndices) + lineV);
                        sb.Append("".RightAlign(widthIndices) + lineV);
                        sb.Append("".RightAlign(widthIndices) + lineV);
                        continue;
                    }

                    runs.Add(RunForValueWithMinMax(s.First().Coverage, "0.00 %", widthIndices, lowestCoverage, highestCoverage, true));
                    runs.Add(new Run(lineV.ToString()));
                    runs.Add(RunForValueWithMinMax(s.First().CI, "0.00", widthIndices, lowestCI, highestCI));
                    runs.Add(new Run(lineV.ToString()));
                    runs.Add(RunForValueWithMinMax(s.First().nCI, "0.00", widthIndices, lowestNCI, highestNCI));
                    runs.Add(new Run(lineV.ToString()));
                    runs.Add(RunForValueWithMinMax(s.First().GI, "0.00", widthIndices, lowestGI, highestGI));
                    runs.Add(new Run(lineV.ToString()));
                    sb.Append(s.First().Coverage.ToString("0.00 %").RightAlign(widthIndices) + lineV);
                    sb.Append(s.First().CI.ToString("0.00").RightAlign(widthIndices) + lineV);
                    sb.Append(s.First().nCI.ToString("0.00").RightAlign(widthIndices) + lineV);
                    sb.Append(s.First().GI.ToString("0.00").RightAlign(widthIndices) + lineV);
                }

                runs.Add(new Run("\n"));
                list.Add(sb.ToString());

                sb.Clear();
            }

            sb.Append(lineV + Translate.GetString("Indices").LeftAlign(widthC1_C2_C3) + lineV);
            for (var i = 0; i < _plansToCompare.Count; i++)
            {
                sb.Append(Translate.GetString("Coverage").RightAlign(widthIndices) + lineV + Translate.GetString("CI").RightAlign(widthIndices) + lineV + Translate.GetString("nCI").RightAlign(widthIndices) + lineV + Translate.GetString("GI").RightAlign(widthIndices) + lineV);
            }

            var header = sb.ToString();

            if (list.Count == 0)
            {
                _paragraphIndices = paragraph;
                return;
            }

            paragraph.Inlines.Add(new Run(GetLine(null, header) + "\n"));
            paragraph.Inlines.Add(new Run(header + "\n"));
            paragraph.Inlines.Add(new Run(GetLine(header, list.First()) + "\n"));

            foreach (var run in runs)
            {
                paragraph.Inlines.Add(run);
            }

            paragraph.Inlines.Add(new Run(GetLine(list.Last(), null)));

            _paragraphIndices = paragraph;
        }

        private void CreateStructures()
        {
            var paragraph = new Paragraph();
            var list = new List<string>();
            var sb = new StringBuilder();
            var runs = new List<Run>();
            var lastType = VOIContourType.Target;

            try
            {
                foreach (var structure in _listOfStructures.OrderBy((s) => s.Value == null ? "zzzz" : (9 - (int)s.Value.Type).ToString("0") + s.Value.Name))
                {
                    if (structure.Value == null)
                        continue;

                    var lowestMin = double.MaxValue;
                    var highestMin = double.MinValue;
                    var lowestMean = double.MaxValue;
                    var highestMean = double.MinValue;
                    var lowestMax = double.MaxValue;
                    var highestMax = double.MinValue;

                    // Not always the first plan has the volume for the structure
                    Plan planToUse = null;

                    foreach (var plan in _plansToCompare)
                    {
                        var planDVData = _planDVData[plan.Value];
                        var v = planDVData.DVData?.Where((sd) => sd.VOIUUID == structure.Key);

                        if (v == null)
                            continue;

                        if (v.Count() > 0)
                            planToUse = plan.Value;

                        if (v.Count() > 0)
                        {
                            lowestMin = Math.Min(lowestMin, v.First().MinDose);
                            highestMin = Math.Max(highestMin, v.First().MinDose);
                            lowestMean = Math.Min(lowestMean, v.First().MeanDose);
                            highestMean = Math.Max(highestMean, v.First().MeanDose);
                            lowestMax = Math.Min(lowestMax, v.First().MaxDose);
                            highestMax = Math.Max(highestMax, v.First().MaxDose);
                        }
                    }

                    if (lastType != structure.Value.Type)
                    {
                        if (list.Count > 0)
                            runs.Add(new Run(GetLine(list.First(), list.First()) + "\n"));
                        lastType = structure.Value.Type;
                    }

                    var type = structure.Value.TypeAsString();
                    var reverse = type == "T";

                    runs.Add(new Run(lineV.ToString()));

                    // Background of type should be the color, we use for the diagram
                    runs.Add(new Run(type.CenterAlign(widthC1)).Highlight(true, null, structure.Value.ColorAsArray(_config)));
                    runs.Add(new Run(lineV.ToString()));

                    // Selected structure should have different color
                    runs.Add(new Run(structure.Value.Name.LeftAlign(widthC2)).Highlight(structure.Value == _selectedStructure, _colorSelectedForeground, _colorSelectedBackground));

                    runs.Add(new Run(lineV.ToString()));
                    runs.Add(new Run((structure.Value.TotalVolume / 1000.0).ToString("#,#0.00").RightAlign(widthC3)));
                    runs.Add(new Run(lineV.ToString()));
                    sb.Append(lineV + "T".CenterAlign(widthC1) + lineV + structure.Value.Name.LeftAlign(widthC2) + lineV);
                    sb.Append((structure.Value.TotalVolume / 1000.0).ToString("#,#0.00").RightAlign(widthC3) + lineV);

                    foreach (var plan in _plansToCompare)
                    {
                        var planDVData = _planDVData[plan.Value];
                        var s = planDVData.DVData?.Where((sd) => sd.VOIUUID == structure.Key);

                        if (s == null || s.Count() == 0)
                        {
                            runs.Add(new Run("".RightAlign(widthDetails)));
                            runs.Add(new Run(lineV.ToString()));
                            runs.Add(new Run("".RightAlign(widthDetails)));
                            runs.Add(new Run(lineV.ToString()));
                            runs.Add(new Run("".RightAlign(widthDetails)));
                            runs.Add(new Run(lineV.ToString()));
                            sb.Append("".RightAlign(widthDetails) + lineV);
                            sb.Append("".RightAlign(widthDetails) + lineV);
                            sb.Append("".RightAlign(widthDetails) + lineV);
                            continue;
                        }

                        // For targets, the values are vice versa. The highest dose is the best.
                        runs.Add(RunForValueWithMinMax(s.First().MinDose, "0.00", widthDetails, lowestMin, highestMin, reverse));
                        runs.Add(new Run(lineV.ToString()));
                        runs.Add(RunForValueWithMinMax(s.First().MeanDose, "0.00", widthDetails, lowestMean, highestMean, reverse));
                        runs.Add(new Run(lineV.ToString()));
                        runs.Add(RunForValueWithMinMax(s.First().MaxDose, "0.00", widthDetails, lowestMax, highestMax, reverse));
                        runs.Add(new Run(lineV.ToString()));
                        sb.Append(s.First().MinDose.ToString("0.00").RightAlign(widthDetails) + lineV);
                        sb.Append(s.First().MeanDose.ToString("0.00").RightAlign(widthDetails) + lineV);
                        sb.Append(s.First().MaxDose.ToString("0.00").RightAlign(widthDetails) + lineV);
                    }

                    runs.Add(new Run("\n"));
                    list.Add(sb.ToString());

                    sb.Clear();
                }
            }
            catch (Exception e)
            {
                _logger.Error(e);
            }

            sb.Append(lineV + Translate.GetString("Structures").LeftAlign(widthC1_C2) + lineV);
            sb.Append(Translate.GetString("Volume").RightAlign(widthC3) + lineV);
            for (var i = 0; i < _plansToCompare.Count; i++)
            {
                sb.Append(Translate.GetString("Min").RightAlign(widthDetails) + lineV + Translate.GetString("Mean").RightAlign(widthDetails) + lineV + Translate.GetString("Max").RightAlign(widthDetails) + lineV);
            }

            var header = sb.ToString();

            paragraph.Inlines.Add(new Run(GetLine(null, header) + "\n"));
            paragraph.Inlines.Add(new Run(header + "\n"));
            paragraph.Inlines.Add(new Run(GetLine(header, list.Count == 0 ? null : list.First()) + "\n"));

            foreach (var run in runs)
            {
                paragraph.Inlines.Add(run);
            }

            paragraph.Inlines.Add(new Run(GetLine(list.Count == 0 ? null : list.Last(), null)));

            _paragraphStructures = paragraph;
        }

        private void UpdateGraphics()
        {
            try
            {
                foreach (var kv in _listOfPlots)
                {
                    plot.Plot.Remove(kv.Value);
                }

                _listOfPlots.Clear();

                plot.Plot.XAxis.Label(Translate.GetString("GraphicsDose"), System.Drawing.Color.White, size: 12);
                plot.Plot.YAxis.Label(Translate.GetString("GraphicsVolume"), System.Drawing.Color.White, size: 12);
                plot.Plot.YAxis.TickLabelFormat((d) => $"{d*100:0} %");

                _plotLineVert.IsVisible = false;
                _highlightedPoint.IsVisible = false;

                if (_selectedPlan == null)
                {
                    plot.Refresh();
                    return;
                }

                plot.Plot.SetAxisLimitsY(0, 1.05);
                plot.Plot.SetAxisLimitsX(0, _planDVData[_selectedPlan].DVHOverallMaxDose);

                foreach (var data in _planDVData[_selectedPlan].DVData)
                {
                    var color = System.Drawing.Color.Black;
                    if (data?.VOIUUID != null && _listOfStructures.ContainsKey(data.VOIUUID))
                    {
                        if (_listOfStructures[data.VOIUUID]?.Color != null)
                        {
                            var colorArray = _listOfStructures[data.VOIUUID].ColorAsArray(_config);
                            if (color != null)
                                color = System.Drawing.Color.FromArgb(colorArray[1], colorArray[2], colorArray[3]);
                        }

                        _listOfPlots[_listOfStructures[data.VOIUUID]] = plot.Plot.AddScatterLines(data.DVHDoseValues, data.DVHVolumePercentValues, color);
                    }
                }

                plot.Refresh();
            }
            catch (Exception e)
            {
                _logger.Error(e);
            }
        }

        private Run RunForValueWithMinMax(double value, string format = "0.0", int width = 10, double min = 0, double max = double.MaxValue, bool reverse = false)
        {
            var run = new Run(value.ToString(format).RightAlign(width));

            // There is only one plan to compare, so use the normal color
            if (min == max)
                return run;

            if (reverse)
            {
                if (value <= min)
                    run.Foreground = _colorRed;
                if (value >= max)
                    run.Foreground = _colorGreen;
            }
            else
            {
                if (value <= min)
                    run.Foreground = _colorGreen;
                if (value >= max)
                    run.Foreground = _colorRed;
            }

            return run;
        }

        private string GetLine(string s1, string s2)
        {
            StringBuilder sb = new StringBuilder();

            if (s1 == null && s2 == null)
                return string.Empty;

            if (s1 != null && s2 != null && s1.Length != s2.Length)
                throw new ArgumentException("First string should have same length than second string");

            int end = s1 != null ? s1.Length - 1 : s2.Length - 1;

            for (var i = 0; i <= end; i++)
            {
                var flagS1 = false;
                var flagS2 = false;

                flagS1 = s1 != null && s1[i] == lineV;
                flagS2 = s2 != null && s2[i] == lineV;

                // Above and below is vertical line
                if (flagS1 && flagS2)
                {
                    if (i == 0)
                        sb.Append(lineL);
                    else if (i == end)
                        sb.Append(lineR);
                    else
                        sb.Append(lineC);
                }

                // Only above is a vertical line
                if (flagS1 && !flagS2)
                {
                    if (i == 0)
                        sb.Append(lineBL);
                    else if (i == end)
                        sb.Append(lineBR);
                    else
                        sb.Append(lineBC);
                }

                // Only below is a vertical line
                if (!flagS1 && flagS2)
                {
                    if (i == 0)
                        sb.Append(lineTL);
                    else if (i == end)
                        sb.Append(lineTR);
                    else
                        sb.Append(lineTC);
                }

                // No vertical lines above and below
                if (!flagS1 && !flagS2)
                {
                    sb.Append(lineH);
                }
            }

            return sb.ToString();
        }

        private void UpdatePatients()
        {
            _logger.Info("Update patients");

            // Save selected patient
            var selectedPatient = (string)cbPatient.SelectedItem;

            cbPatient.Items.Clear();
            cbPatient.SelectedIndex = 0;

            // Get all patients from Broker
            if (cbArchived.IsChecked == true)
            {
                _patientList = _client.GetAllPatients();
            }
            else
            {
                _patientList = _client.GetPatientsWithStatus();
            }

            if (_patientList == null)
                return;

            foreach (var patient in _patientList.OrderBy(p => p.MedicalId).Reverse())
            {
                var text = patient.MedicalId.Trim() + " - " + patient.LastName.Trim() + ", " + patient.FirstName.Trim();
                cbPatient.Items.Add(text);
            }

            // Select old selected patient
            if (cbPatient.Items.Contains(selectedPatient))
            {
                cbPatient.SelectedIndex = cbPatient.Items.IndexOf(selectedPatient);
                UpdatePlansForPatient(selectedPatient.Substring(0, selectedPatient.IndexOf(" - ")));
            }
        }

        private void UpdatePlansForPatient(string medicalId)
        {
            _logger.Info($"Load plans for Patient '{medicalId}'");

            _plans.Clear();
            _planList = null;

            var patient = _patientList.Where(p => p.MedicalId.Trim().Equals(medicalId)).FirstOrDefault();

            if (patient == null)
            {
                _logger.Error($"Patient {medicalId} not found");
                return;
            }

            _planList = _client.GetPlansForPatient(patient);

            if (_planList == null)
            {
                return;
            }

            foreach (var plan in _planList)
            {
                var planName = plan.PlanName.Trim();

                if (string.IsNullOrEmpty(planName))
                    continue;

                if (_plans.ContainsKey(planName))
                {
                    // Plan already there, so compare save dates
                    if (_plans[planName].LastSavedTime > plan.LastSavedTime)
                    {
                        continue;
                    }
                    else
                    {
                        // Remove older plan
                        _plans.Remove(planName);
                    }
                }

                _plans.Add(planName, plan);
            }

            CreateListOfPlans();

            UpdateDocument();
            UpdateGraphics();
        }

        private void CreateListOfPlans()
        {
            _logger.Info($"Create list of plans");

            var checkedPlanNames = new List<string>();

            UIElement[] childrens = new UIElement[spPlans.Children.Count];

            spPlans.Children.CopyTo(childrens, 0);  

            // Save already checked plan names
            foreach (var cb in childrens)
            {
                if ((bool)((CheckBox)cb).IsChecked)
                {
                    checkedPlanNames.Add(((CheckBox)cb).Content.ToString());
                }

                spPlans.Children.Remove(cb);
            }

            // Create new checkboxes
            foreach (var plan in _plans)
            {
                var cb = new CheckBox
                {
                    Content = plan.Key,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };

                cb.Checked += UpdatePlans;
                cb.Unchecked += UpdatePlans;

                cb.IsChecked = checkedPlanNames.Contains(plan.Key);

                spPlans.Children.Add(cb);
            }

            _paragraphHeader = new Paragraph();
            _paragraphIndices = new Paragraph();
            _paragraphStructures = new Paragraph();

            UpdatePlans(this, new RoutedEventArgs());
        }

        private void UpdateDocument()
        {
            rtOutput.Document = CreateDocument();
        }

        private void UpdatePlans(object sender, RoutedEventArgs e)
        {
            _plansToCompare.Clear();
            _selectedPlan = null;

            foreach (var obj in spPlans.Children)
            {
                var cb = obj as CheckBox;
                if (cb != null && (bool)cb.IsChecked)
                {
                    _plansToCompare.Add((string)cb.Content, _plans[(string)cb.Content]);
                }
            }

            LoadPlanData();
            LoadDVData();
            LoadPlanStatus();
            LoadBeamData();
            LoadStructures();

            CreateHeader();
            CreateIndices();
            CreateStructures();

            UpdateDocument();
            UpdateGraphics();
        }

        private void LoadPlanData()
        {
            // Get plan data
            foreach (var plan in _plansToCompare)
            {
                // Read data only once
                //if (_planData.ContainsKey(plan.Value))
                //    continue;

                _planData[plan.Value] = _client.GetPlanDataForPlan(plan.Value);
            }
        }

        private void LoadDVData()
        {
            // Get dose volume data
            foreach (var plan in _plansToCompare)
            {
                // Read data only once
                //if (!_planDVData.ContainsKey(plan.Value))
                //    continue;

                _planDVData[plan.Value] = _client.GetDVDataForPlan(plan.Value);
            }
        }

        private void LoadPlanStatus()
        {
            // Get plan status
            foreach (var plan in _plansToCompare)
            {
                // Read data only once
                //if (!_planStatus.ContainsKey(plan.Value))
                //    continue;

                _planSummary[plan.Value] = _client.GetPlanSummaryForPlan(plan.Value);
            }
        }

        private void LoadBeamData()
        {
            // Get beam data for each plan
            foreach (var plan in _plansToCompare)
            {
                // Read data only once
                //if (!_planStatus.ContainsKey(plan.Value))
                //    continue;

                _planBeamData[plan.Value] = _client.GetBeamsForPlan(plan.Value);
            }
        }

        private void LoadStructures()
        {
            _listOfStructures.Clear();

            foreach (var plan in _plansToCompare)
            {
                var dvData = _planDVData[plan.Value];

                if (dvData?.DVData == null)
                {
                    continue;
                }

                foreach (var structure in dvData.DVData)
                {
                    if (!_listOfStructures.ContainsKey(structure.VOIUUID) && !structure.VOIUUID.StartsWith("st"))
                    {
                        _listOfStructures.Add(structure.VOIUUID, null);
                    }
                }
            }

            // Get names of structures
            foreach (var plan in _plansToCompare)
            {
                _planVOIs[plan.Value] = _client.GetVOIsForPlan(plan.Value);

                if (_planVOIs[plan.Value]?.VOISet?.VOIs == null)
                {
                    continue;
                }

                foreach (var voi in _planVOIs[plan.Value].VOISet.VOIs)
                {
                    if (_listOfStructures.ContainsKey(voi.UUID))
                        _listOfStructures[voi.UUID] = voi;
                }
            }
        }

        private void CreateConfig()
        {
            _config = CompareConfig.LoadConfigData();
        }

        private void CreateLogger()
        {
            var config = new NLog.Config.LoggingConfiguration();

            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget("logfile") { FileName = System.IO.Path.ChangeExtension(System.IO.Path.GetFileName(System.Reflection.Assembly.GetEntryAssembly().Location), "log").Replace(".log", "." + ZapClient.Helpers.Network.GetHostName() + ".log") };

            // Rules for mapping loggers to targets            
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, logfile);

            // Apply config           
            NLog.LogManager.Configuration = config;
        }

        // See https://stackoverflow.com/questions/9264398/how-to-calculate-wpf-textblock-width-for-its-known-font-size-and-characters
        private Size MeasureString(string candidate)
        {
            DpiScale? dpiInfo;

            lock (_sync)
                dpiInfo = _dpiInfo;

            if (dpiInfo == null)
                throw new InvalidOperationException("Window must be loaded before calling MeasureString");

            var formattedText = new FormattedText(candidate, CultureInfo.CurrentUICulture,
                                                  FlowDirection.LeftToRight,
                                                  new Typeface(rtOutput.FontFamily,
                                                               rtOutput.FontStyle,
                                                               rtOutput.FontWeight,
                                                               rtOutput.FontStretch),
                                                  rtOutput.FontSize,
                                                  Brushes.Black,
                                                  (double)dpiInfo?.PixelsPerDip);

            return new Size(formattedText.Width, formattedText.Height);
        }

        protected override void OnDpiChanged(DpiScale oldDpiScaleInfo, DpiScale newDpiScaleInfo)
        {
            lock (_sync)
                _dpiInfo = newDpiScaleInfo;
        }

        static string customTickFormatter(double position)
        {
            return $"{position:0} %";
        }

        public (string, string) GetUsernameAndPassword(string oldUsername, string oldPassword)
        {
            var dialog = new LoginWindow();

            dialog.lblUsername.Content = Translate.GetString("Username");
            dialog.lblPassword.Content = Translate.GetString("Password");
            dialog.btnLogin.Content = Translate.GetString("Login");
            dialog.btnCancel.Content = Translate.GetString("Cancel");

            dialog.textUsername.Text = oldUsername;
            dialog.textPassword.Password = oldPassword;

            if (string.IsNullOrEmpty(oldUsername))
            {
                dialog.textUsername.Focus();
            }
            else
            {
                dialog.textPassword.Focus();
            }

            if (!(bool)dialog.ShowDialog())
            {
                return (oldUsername, oldPassword);
            }

            return (dialog.textUsername.Text, dialog.textPassword.Password);
        }

        static string GetIPAdress()
        {
            // Retrive the Name of HOST
            var hostName = Dns.GetHostName();
            // Get the IP
            string result = string.Empty;
            
            foreach (var ip in Dns.GetHostAddresses(hostName))
            {
                // ZAP uses always a 10.0.0.255 adress
                if (ip.ToString().StartsWith("10."))
                    result = ip.GetAddressBytes()[3].ToString("000");
            }

            return result;
        }
    }
}
