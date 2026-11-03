using Snapper.Core.App;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Snapper.IconGen <output.ico>");
    return 2;
}
File.WriteAllBytes(args[0], AppIconDesign.WriteIco());
return 0;
