using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace TripsDataView
{
    public class Utils
    {
        public static void createAndDeleteFiles(string fileName, string header, string fileNamePattern, string path)
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }

            //Mod.log.Info($"Creating file: {fileName}");
            using (StreamWriter sw = File.AppendText(fileName))
            {
                sw.WriteLine(header);
            }

            // Get the files
            DirectoryInfo info = new DirectoryInfo(Mod.outputPath);
            FileInfo[] files = info.GetFiles(fileNamePattern + "*");

            // Sort by creation-time descending 
            Array.Sort(files, delegate (FileInfo f1, FileInfo f2)
            {
                return f1.CreationTime.CompareTo(f2.CreationTime);
            });

            while (files.Length > Mod.setting.numOutputs)
            {
                Mod.log.Info($"Deleting: {files[0].FullName}");
                File.Delete(files[0].FullName);

                // Get the files
                info = new DirectoryInfo(Mod.outputPath);
                files = info.GetFiles(fileNamePattern + "*");

                // Sort by creation-time descending 
                Array.Sort(files, delegate (FileInfo f1, FileInfo f2)
                {
                    return f1.CreationTime.CompareTo(f2.CreationTime);
                });
            }
        }
    }
}
