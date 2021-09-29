using System;
using System.IO;
using System.Threading.Tasks;

namespace DecryptTencentEduVideo
{
    class Program
    {
        static async Task Main(string[] args)
        {
            foreach (var file in Directory.GetFiles(@"E:\txdownload"))
            {
                var filePath = new FileInfo(file).Directory;
                await DecryptTencentEduVideoHandle.StartAsync(file, Path.Combine(filePath?.FullName, "123"), true);
            }

            Console.WriteLine("Hello World!");
        }
    }
}