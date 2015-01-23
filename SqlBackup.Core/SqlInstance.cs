using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer;
using System.IO;

namespace SqlBackup.Core
{
    public class SqlInstance : IDisposable
    {
        Server smo;
        string serverName;

        public SqlInstance(string name)
        {
            serverName = name;
            smo = Connect(serverName);
        }

        Server Connect(string name)
        {
            return new Server(new ServerConnection(name)
            {
                StatementTimeout = int.MaxValue
            });
        }

        public void Backup(string backupRootPath, BackupType backupType)
        {
            var pathSQL = Path.Combine(backupRootPath, serverName.Replace(@"\", @"^"));
            Disk.VerifyDirectory(pathSQL);

            var lockfile = Path.Combine(pathSQL, "backup_lock.pid");
            if (File.Exists(lockfile))
            {
                var existingProcessID = File.ReadAllText(lockfile);
                throw new BackupException(string.Format("Unable to backup {0} due to lock file with process id {1}.", serverName, existingProcessID));
            }
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(lockfile, pid.ToString());

            // get databases for backup
            // todo: filter by more statuses
            var databases = smo.Databases.Cast<Database>().Where(o => !o.Name.Equals("tempdb", StringComparison.InvariantCultureIgnoreCase)).Where(o => !o.Status.HasFlag(DatabaseStatus.Offline)).ToList();

            var databasesForFullBackup = databases;
            var databasesForLogBackup = new List<Database>();
            if (backupType == BackupType.TransactionLog)
            {
                // invoke full backup on log backup type if full backup has never been made
                databasesForFullBackup = databases.Where(o => o.LastBackupDate == DateTime.MinValue).ToList();
                databasesForLogBackup = databases.Where(o => o.RecoveryModel != RecoveryModel.Simple).ToList();
            }

            // Perform Full Backups
            // todo: see if this is thread safe and make parallel
            foreach (var database in databasesForFullBackup)
            {
                Log.Info(string.Format("Starting full backup of {0} {1}", database.Name, DateTime.Now.ToString()));

                var dbPathSQL = Path.Combine(pathSQL, database.Name);
                Disk.VerifyDirectory(dbPathSQL);

                var isIncremental = backupType == BackupType.Differential;
                if (database.Name.Equals("master", StringComparison.InvariantCultureIgnoreCase))
                    isIncremental = false;
                if (database.LastBackupDate == DateTime.MinValue)
                    isIncremental = false;

                var extension = isIncremental ? ".diff.bak" : ".full.bak";

                var backupFiles = new List<string>();
                var backupBaseName = string.Format("{0}_{1}", database.Name, DateTime.Now.ToString("yyyy-MM-dd-HHmm-ss"));
                int backupFilesCount = 1;
                if (database.Size > 10240 && !isIncremental)
                    backupFilesCount = (int)Math.Floor(database.Size / 20240);
                if (backupFilesCount == 1)
                {
                    backupFiles.Add(string.Format("{0}{1}", backupBaseName, extension));
                }
                else
                {
                    Enumerable.Range(1, backupFilesCount).ToList().ForEach(o =>
                    {
                        backupFiles.Add(string.Format("{0}_{1}{2}", backupBaseName, o.ToString("D2"), extension));
                    });
                }

                var backupFilesSQL = backupFiles.Select(o => Path.Combine(dbPathSQL, o)).ToList();

                // run SQL backup
                try
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    InvokeSqlBackup(database, backupFilesSQL, BackupActionType.Database, isIncremental, "Full Database Backup", string.Format("Full backup of {0}", database.Name), false);
                    timer.Stop();
                    var totalBackupSize = backupFilesSQL.Sum(o => (new FileInfo(o)).Length);
                    var backupWriteSpeedKB = (totalBackupSize / timer.Elapsed.TotalSeconds) / 1024;
                    var backupWriteSpeedMB = totalBackupSize / Math.Pow(1024, 2);
                    Log.Info(string.Format("Complete\tTotal Size: {0} MB\tSpeed: {1} KB/s", totalBackupSize.ToString("N2"), backupWriteSpeedKB.ToString("N2")));
                }
                catch (Exception ex)
                {
                    var message = string.Format("{0} Error performing {1} backup of {2} on {3}. {4}", 
                                                    DateTime.Now.ToString(),
                                                    backupType,
                                                    database.Name,
                                                    smo.Name,
                                                    ex.GetAllMessages());
                    Log.Error(message);
                }
            }

            // Perform Log Backups
            foreach (var database in databasesForLogBackup)
            {
                Log.Info(string.Format("Starting Log backup of {0} {1}", database.Name, DateTime.Now.ToString()));

                var dbPathSQL = Path.Combine(pathSQL, database.Name);
                Disk.VerifyDirectory(dbPathSQL);

                var extension = ".trn";

                var backupFiles = new List<string>()
                {
                    string.Format("{0}_{1}{2}", database.Name, DateTime.Now.ToString("yyyy-MM-dd-HHmm-ss"), extension)
                };

                var backupFilesSQL = backupFiles.Select(o => Path.Combine(dbPathSQL, o)).ToList();

                // run SQL backup
                try
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    InvokeSqlBackup(database, backupFilesSQL, BackupActionType.Log, false, "Log Database Backup", string.Format("Log backup of {0}", database.Name), false);
                    timer.Stop();
                    var totalBackupSize = backupFilesSQL.Sum(o => (new FileInfo(o)).Length);
                    var backupWriteSpeedKB = (totalBackupSize / timer.Elapsed.TotalSeconds) / 1024;
                    var backupWriteSpeedMB = totalBackupSize / Math.Pow(1024, 2);
                    Log.Info(string.Format("Complete\tTotal Size: {0} MB\tSpeed: {1} KB/s", totalBackupSize.ToString("N2"), backupWriteSpeedKB.ToString("N2")));
                }
                catch (Exception ex)
                {
                    var message = string.Format("{0} Error performing {1} backup of {2} on {3}. {4}",
                                                    DateTime.Now.ToString(),
                                                    backupType,
                                                    database.Name,
                                                    smo.Name,
                                                    ex.GetAllMessages());
                    Log.Error(message);
                }
            }

            File.Delete(lockfile);
        }

        // should be run weekly
        public void PerformMaintenance()
        {
            // clean backup history in SQL Server
            var historyCleanQuery = string.Format("EXEC sp_delete_backuphistory {0}", DateTime.Now.AddDays(-35).ToString());
            smo.Databases["msdb"].ExecuteNonQuery(historyCleanQuery);

            // todo: update statistics if autoupdate is off for a database
        }

        void InvokeSqlBackup(Database database, List<string> backupFiles, BackupActionType action, bool incremental = false, string backupSetName = "", string backupSetDescription = "", bool copyOnly = false)
        {
            if (database.Name.Equals("master", StringComparison.InvariantCultureIgnoreCase) && incremental)
                incremental = false;  // cannot perform differential backup on [master]

            var backup = new Backup()
            {
                Database = database.Name,
                Action = action,
                BackupSetDescription = backupSetDescription,
                BackupSetName = backupSetName,
                MediaDescription = "Disk",
                Incremental = incremental
            };
            backupFiles.ForEach(o =>
            {
                backup.Devices.AddDevice(o, DeviceType.File);
            });

            // todo: make compression optional
            // enable compression if supported.  must be >= SQL 2008 R2 (10.50)
            if (smo.Version.Major >= 10 && smo.Version.Minor >= 50)
                backup.CompressionOption = BackupCompressionOptions.On;

            backup.SqlBackup(smo);  // todo: use async
            backup.Wait();
        }
    
        public void Dispose()
        {
            smo = null;
        }

        //public static List<string> SystemDatabases
        //{
        //    get
        //    {
        //        return new List<string>()
        //        {
        //            "master",
        //            "model",
        //            "tempdb",
        //            "msdb"
        //        };
        //    }
        //}
    }
}
