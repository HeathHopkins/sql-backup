using System;

namespace SqlBackup.Core
{
    public enum BackupType
    {
        Full = 0,
        Differential = 1,
        TransactionLog = 2
    }
}
