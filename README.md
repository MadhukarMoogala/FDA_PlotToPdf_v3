## FDA_PlotToPdf_v3

[![Data-Management](https://img.shields.io/badge/Data%20Management-v1-green.svg)](http://developer.autodesk.com/)
[![Design-Automation](https://img.shields.io/badge/Design%20Automation-v3-green.svg)](http://developer.autodesk.com/)
[![netCore](https://img.shields.io/badge/netcore-3.1-green)](https://dotnet.microsoft.com/download/dotnet-core/current/runtime)
### Description
A CLI utility based on .NET Core technology to print a AutoCAD drawing in to a pdf.

Uses [Forge Design Automation V3](https://forge.autodesk.com/en/docs/design-automation/v3)

This sample uses Shared [Activity](https://forge.autodesk.com/en/docs/design-automation/v3/developers_guide/basics/#typical-workflows) `AutoCAD.PlotToPDFDwgzip+prod`

### Working Gif

![MyGif](https://github.com/MadhukarMoogala/FDA_PlotToPdf_v3/blob/master/plottopdf.gif)


### Prerequisites
1. **Forge Account**: Learn how to create a Forge Account, activate subscription and create an app at [this tutorial](http://learnforge.autodesk.io/#/account/). 
2. **Visual Code**: Visual Code (Windows or MacOS)
3. **ngrok**: Routing tool, [download here](https://ngrok.com/)
4. **.netcore 3.1**: [dotnet core SDK](https://dotnet.microsoft.com/download/dotnet-core/current/runtime) 

### Instructions To Build

```bash
git clone https://github.com/MadhukarMoogala/FDA_PlotToPdf_v3.git
cd FDA_PlotToPdf_v3\client
notepad appsettings.users.json feed with your Forge Credentials
dotnet build
dotnet run -i "<inputZipFileWithDrawings>" -o "<outputFolder>"
```
`appsettings.users.json`

```
{
  "Forge": {
    "ClientId": "ForgeClientId",
    "ClientSecret": "ForgeClientSecret"
  }
}
```

`launchsettings.json`

```
{
  "profiles": {
    "ClientV3": {
      "commandName": "Project",
      "commandLineArgs": "-i \"D:\\Work\\Forge\\FDA_PlotToPdf_v3\\Client\\inputs\\sample.zip\" -o \"D:\\Work\\Forge\\FDA_PlotToPdf_v3\\Client\\outputs\"",
      "environmentVariables": {
        "FORGE_CLIENT_SECRET": "ForgeClientId",
        "FORGE_CLIENT_ID": "ForgeClientSecret"
      }
    }
  }
}
```




#### Instructions To Debug

```
notepad launchsettings.json
```
### License
This sample is licensed under the terms of the [MIT License](http://opensource.org/licenses/MIT). Please see the [LICENSE](LICENSE) file for full details.

### Written by
Madhukar Moogala, [Forge Partner Development](http://forge.autodesk.com)  @galakar




