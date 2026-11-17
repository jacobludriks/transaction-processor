using System.Globalization;
using RunReport;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
return Cli.Run(args, Console.Out, Console.Error);
