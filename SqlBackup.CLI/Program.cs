using Fclp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SqlBackup.Core;

namespace SqlBackup.CLI
{
    class Program
    {
        static void Main(string[] args)
        {
            var argumentParseResult = ParseArguments(args);
            if (argumentParseResult.Item1.HasErrors)
            {
                Console.WriteLine("There was an error parsing arguments.");
                argumentParseResult.Item1.Errors.ToList().ForEach(item =>
                {
                    Console.WriteLine(item.Option.Description);
                });
                return;
            }
            var arguments = argumentParseResult.Item2.Object;

            Console.WriteLine(string.Format("Server: {0}  BackupLocation: {1}  BackupType: {2}",
                arguments.Server,
                arguments.BackupLocation,
                arguments.BackupType));
        
            using (var server = new SqlInstance(arguments.Server))
            {
                server.Backup(arguments.BackupLocation, arguments.BackupType);
            }
        
        }

        static Tuple<ICommandLineParserResult, FluentCommandLineParser<CommandLineArguments>> ParseArguments(string[] args)
        {
            var parsedArgs = new FluentCommandLineParser<CommandLineArguments>();

            parsedArgs.Setup(arg => arg.Server)
                .As('s', "server")
                .WithDescription("The server's address.")
                .Required();

            parsedArgs.Setup(arg => arg.BackupLocation)
                .As('l', "location")
                .WithDescription("Specifiy the backup location.")
                .Required();

            parsedArgs.Setup(arg => arg.BackupType)
                .As('t', "type")
                .SetDefault(BackupType.Full);

            var result = parsedArgs.Parse(args);
            return new Tuple<ICommandLineParserResult, FluentCommandLineParser<CommandLineArguments>>(result, parsedArgs);
        }
    }

    public class CommandLineArguments
    {
        public string Server { get; set; }
        public string BackupLocation { get; set; }
        public BackupType BackupType { get; set; }
    }
}
