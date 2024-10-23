namespace SabTool.CLI;

using SabTool.CLI.Commands;
using System.CommandLine;

public sealed class Program
{
	public static void Main(string[] args)
	{
		// First we define the root-level command
		var rootCommand = new RootCommand("Tool for packing and unpacking The Saboteur assets");

		// Next, we define the common positional arguments
		var gameDir = new Argument<DirectoryInfo>("game-directory", description: "Path to your Saboteur install directory") ;
		var inputFile = new Argument<FileInfo>("input-file", description: "File to pack");
		var packPath = new Argument<FileInfo>("pack-file", description: "Path to pack file");
		var inputDir = new Argument<DirectoryInfo>("input-dir", description: "Directory to pack");
		var outputDir = new Argument<DirectoryInfo>("output-directory", description: "Path to dump files to");


		// Next, the blueprints category
		var blueprints = new Command("blueprints", "[HELP GOES HERE]");
		rootCommand.AddCommand(blueprints);

		// Blueprints unpack command
		var blueUnpack = new Command("unpack", "Unpacks blueprints") { gameDir, outputDir };

		blueUnpack.SetHandler( (gameDirValue, outputDirValue) => {
				new BlueprintsCategory().Unpack( new string[] { gameDirValue.ToString(), outputDirValue.ToString() } );
		}, gameDir, outputDir );
		blueprints.AddCommand(blueUnpack);

		// Blueprints pack command
		var bluePack = new Command("pack", "Packs blueprints") { gameDir, inputFile };

		bluePack.SetHandler( (gameDirValue, inputFileValue) => {
			new BlueprintsCategory().Pack(new string[] { gameDirValue.ToString(), inputFileValue.ToString() });
		}, gameDir, inputFile );
		blueprints.AddCommand(bluePack);

		// Blueprints dump command
		var blueDump = new Command("dump", "[HELP GOES HERE]") { gameDir };
		blueDump.SetHandler( (gameDirValue) => {
				new BlueprintsCategory().Dump(new[] {gameDirValue.ToString()});
		}, gameDir );
		blueprints.AddCommand(blueDump);

		// megapack
		var megaPack = new Command("megapack", "[HELP GOES HERE]");

		var megaList = new Command("list", "List available megapacks") { gameDir };
		megaList.SetHandler( (gameDirValue) => {
			new MegapackCategory().List(new[] { gameDirValue.ToString() });
		}, gameDir );
		megaPack.Add(megaList);

		var megaUnpack = new Command("unpack", "Unpacks megapack") { gameDir, outputDir };
		megaUnpack.SetHandler( (gameDir, outputDir) => {
				new MegapackCategory().Unpack(new[] { gameDir.ToString(), outputDir.ToString() });
		}, gameDir, outputDir  );
		megaPack.Add(megaUnpack);
		rootCommand.Add(megaPack);

		var megaPackPack = new Command("pack", "Packs a file into a megapack") { gameDir, inputDir };
		megaPackPack.SetHandler( (gameDir, inputDir) => {
				new MegapackCategory().Pack(new[] { gameDir.ToString(), inputDir.ToString() });
		}, gameDir, inputDir ); 
		megaPack.Add(megaPackPack);

		var looseFiles = new Command("loose-files", "[HELP GOES HERE]");

		var looseUnpack = new Command("unpack", "Unpack loose files file") { gameDir, outputDir };
		looseUnpack.SetHandler( (gameDir, outputDir) => {
				new LooseFilesCategory().Unpack(new[] { gameDir.ToString(), outputDir.ToString() });
		}, gameDir, outputDir );
		looseFiles.Add(looseUnpack);
		rootCommand.Add(looseFiles);

		var packFiles = new Command("pack", "[HELP GOES HERE]");
		var packUnpack = new Command("unpack", "Unpack a pack file") { gameDir, packPath, outputDir }; 
		packUnpack.SetHandler( (gameDir, packPath, outputDir) => {
				new PackCategory().Unpack(new[] { gameDir.ToString(), packPath.ToString(), outputDir.ToString() });
		}, gameDir, packPath, outputDir );
		packFiles.Add(packUnpack);
		rootCommand.Add(packFiles);

		// And we execute
		rootCommand.Invoke(args);
	}
}
