# ZapPlanComparison

## Description
ZapPlanComparison is to compare two or more plans for the ZAP-X system

## Installation
Copy the application and belonging DLLs into an folder. Add the files zsClient.dll
and zsUtilities.dll from your TPS system.

To work correct, you have to add a ZapClient.cfg file (see for this the ZapClient 
library), which is in JSON format and contains server IP and port, username and 
perhaps a password. If you want a different culture then the system one you could 
add this too. Additionally you could provide colors for special structure names. 
This file must be in the same folder as the application.

If you don't provide username or password in the config file, the application
asks for it at the beginning.

## How to use
First select the patient, for whom you want to compare plans. Than select one, 
two or more plans to compare. If you want to see the DVHs, double click into the 
plan name in the table header. It is shown with blue background and the DVHs are
shown. If you want to check some values of a special structure in the DVHs, then
double click on the structure name. It gets a blue background. Now go to the DVHs 
and move with the mouse around.