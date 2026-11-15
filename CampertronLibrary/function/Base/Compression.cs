using System.IO.Compression;

namespace CampertronLibrary.function.Base
{
    public static class Compression
    {
        //the RIDB export is extracted into a folder of Campertron's own in LocalApplicationData, next to CampertronConfig
        //and CampertronCache. Only that folder is cleared here and deleted after the import (PopulateDbFromRIDBFiles); the
        //generic "Extract" used before could be another program's folder, and it was deleted recursively (TD-48)
        public const string ExtractFolderName = "CampertronExtract";
        public static String Unzip(String Source)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String ExtractPath = System.IO.Path.Join(path, ExtractFolderName);
            if(Directory.Exists(ExtractPath))
            {
                Directory.Delete(ExtractPath, true);
            }
            string dirName = ExtractPath;
            string zipName = Source;
            ZipFile.ExtractToDirectory(zipName, dirName, true);
            if (File.Exists(Source))
            {
                File.Delete(Source);
            }
            return ExtractPath;
        }
    }
}