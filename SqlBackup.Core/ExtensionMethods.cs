using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlBackup.Core
{
    public static class ExtensionMethods
    {
        public static string GetAllMessages(this Exception ex)
        {
            if (ex == null)
                return null;

            var sb = new StringBuilder();
            var tmp = ex;
            sb.AppendLine(tmp.Message);
            tmp = tmp.InnerException;
            while (tmp != null)
            {
                sb.AppendLine(tmp.Message);
                tmp = tmp.InnerException;
            }
            return sb.ToString();
        }
    }
}
