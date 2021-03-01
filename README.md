# Eco Civics Import Export Mod
A server mod for Eco 9.2 that allows admins to export the supported civics (listed below) from a server to json files, where they can be copied to another server and re-imported.

Supported civics:
- Laws
- Election Processes
- Elected Titles
- Demographics
- Constitutional Amendments
- District Maps (only compatible when the world size is equivalent)

## Installation

- Copy the `EcoCivicsImportExportMod.dll` file to `Mods` folder of the dedicated server.
- Restart the server.

## Usage

All chat commands require admin privileges.

### Exporting Civics
The `export` command will serialise the specific civic object to a json file in the server's working directory, under a folder called `civics`.

`/civics export <civictype>,<id>`
e.g. `/civics export law,10`

To find the ID of a civic, tag it in chat and hover it, the ID is displayed at the bottom of the popup.
`civictype` must be one of:
- `law`
- `electionprocess`
- `electedtitle`
- `demographic`
- `constitution`
- `amendment`
- `districtmap`

The filename of the exported file will be as follows (relative to the server's working directory): `civics/<civictype>-<id>.json`. The command will inform you of this filename if serialisation is successful.

You can rename the file outside of the game if you wish, as the name of the file is specified in the import command.

The `exportallof` command will serialise all civic objects of a type. It's the equivalent of running the `export` command for each civic object individually.

`/civics exportallof <civictype>`
e.g. `/civics exportallof law`

The `exportall` command will serialise all civic objects of all types. It's the equivalent of running the `exportallof` command for each civic type individually.

`/civics exportall`

### Importing Civics
The import command will attempt to deserialise a civic object from the specified json file. If it fails at any stage, the civic object (if it managed to created one) will be immediately destroyed with no side effects.

`/civics import <filename>`
e.g. `/civics import law-10.json`

The civic object will be given draft status with the command executor as the owner, and put in the first available civic slot (e.g. a law will go to the first available Court). If there are no slots available, the command will fail. The executor of the command should have civic privileges to propose changes to civics of that type, or they may not be able to actually bring the imported civic to life.

The civic object may have dependencies on other objects - for example, a law may reference a bank account or a district map, or an election process may reference a demographic. All dependencies must be present at the point of running the import command, or the command will fail. Dependencies are resolved via name - so if a law references a district called "Main Roads" and the server has a district called "Roads" instead, this will not work - either the district will need to be renamed for the dependency to be resolved, or the json file will need to be manually amended. Dependencies can be safely renamed after the import is complete.

## Building from Source

### Windows

1. Login to the [Eco Website](https://play.eco/) and download the latest modkit
2. Extract the modkit and copy the dlls from `ReferenceAssemblies` to `eco-dlls` in the root directory (create the folder if it doesn't exist)
3. Open `EcoCivicsImportExportMod.sln` in Visual Studio 2019
4. Build the project in Visual Studio
5. Find the artifact in `EcoCivicsImportExportMod\bin\{Debug|Release}\netcoreapp3.1`

### Linux

1. Run `fetch-eco-reference-assemblies.sh`
2. Enter the `EcoCivicsImportExportMod` directory and run:
`dotnet restore`
`dotnet build`
3. Find the artifact in `EcoCivicsImportExportMod/bin/{Debug|Release}/netcoreapp3.1`

## License
[MIT](https://choosealicense.com/licenses/mit/)