using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlBackup.Core
{
    public class BackupException : Exception
    {
        public BackupException()
            : base()
        { }

        public BackupException(string message)
            : base(message)
        { }

        public BackupException(string message, Exception innerException)
            : base(message, innerException)
        { }
    }
}
