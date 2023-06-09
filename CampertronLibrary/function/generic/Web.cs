namespace CampertronLibrary.function.generic
{
    public static class Web
    {
        public static async Task<String> Download(String url, String FileName)
        {
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            String DownloadPath = System.IO.Path.Join(path, FileName);
            if (File.Exists(DownloadPath))
            {
                File.Delete(DownloadPath);
            }
            var httpClient = new HttpClient();
            using (var stream = await httpClient.GetStreamAsync(url))
            {
                using (var fileStream = new FileStream(DownloadPath, FileMode.CreateNew))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
            return DownloadPath;
        }
    }
}