using System.IO.Compression;

namespace CampertronLibrary.function.generic
{
    public static class Compression
    {
        public static String Unzip(String Source)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String ExtractPath = System.IO.Path.Join(path, "Extract\\");
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