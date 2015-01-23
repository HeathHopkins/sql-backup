using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SqlBackup.Core
{
    public class Disk
    {
        /// <summary>
        /// Verifies that the directory exists on the disk by creating it if it doesn't exist.
        /// </summary>
        /// <param name="path">The directory to verify or create.</param>
        public static void VerifyDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
        }
        
        /// <summary>
        /// Tests if a file is writable.
        /// </summary>
        /// <param name="file"></param>
        /// <returns>True if the file is writable, otherwise false.</returns>
        public static bool IsWritable(string file)
        {
            var fileAttributes = File.GetAttributes(file);
            if ((fileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                throw new BackupException(string.Format("{0} is not a file.", file));
            try
            {
                using (var fs = File.OpenWrite(file))
                {
                    fs.Close();
                }
                return true;
            }
            catch 
            {
                return false;
            }
        }
    }
}
