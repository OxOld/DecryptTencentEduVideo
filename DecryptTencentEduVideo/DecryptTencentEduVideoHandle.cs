using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace DecryptTencentEduVideo
{
    /// <summary>
    /// 解密 腾讯课堂视频 m3u8.sqlite
    /// </summary>
    public static class DecryptTencentEduVideoHandle
    {
        private class Caches
        {
            public string Key { get; set; }
            public byte[] Value { get; set; }
        }

        private class OrderByCaches
        {
            public int Id { get; set; }
            public byte[] Value { get; set; }
        }

        /// <summary>
        /// 开始解密
        /// </summary>
        /// <param name="decryptVideoFilePath">要解密的文件路径 如: E:/video/123.m3u8.sqlite</param>
        /// <param name="saveVideoFilePath">解密后文件保存路径 如：E:/video/save</param>
        /// <param name="isMerge">是否合并 ts 为一个文件。</param>
        public static async Task StartAsync(string decryptVideoFilePath, string saveVideoFilePath, bool isMerge)
        {
            if (Path.GetExtension(decryptVideoFilePath) != ".sqlite")
            {
                Console.WriteLine("不是 m3u8.sqlite 文件, 跳过。" + decryptVideoFilePath);
                return;
            }

            var freeSql = new FreeSql.FreeSqlBuilder()
                .UseConnectionString(FreeSql.DataType.Sqlite,
                    $"Data Source={decryptVideoFilePath}")
                .UseAutoSyncStructure(false) //自动同步实体结构到数据库
                .Build();

            var caches = await freeSql.Select<Caches>().ToListAsync();

            var aesKey = Array.Empty<byte>();
            var extXKey = "";
            var tsList = new List<OrderByCaches>();
            foreach (var cache in caches)
            {
                if (cache.Key.Contains("get_dk?edk="))
                {
                    aesKey = cache.Value;
                    continue;
                }

                if (cache.Key.Contains("m3u8"))
                {
                    //#EXT-X-KEY:METHOD=AES-128,URI="key.key",IV=0x00000000000000000000000000000000

                    var extXKeyStr = Encoding.Default.GetString(cache.Value)
                        .Split("\n")
                        .FirstOrDefault(x => x.StartsWith("#EXT-X-KEY"));
                    const string defaultIv = "0x00000000000000000000000000000000";
                    if (extXKeyStr == null)
                    {
                        extXKey = defaultIv;
                    }
                    else
                    {
                        extXKey = extXKeyStr.Substring(
                            extXKeyStr.IndexOf("IV=", StringComparison.OrdinalIgnoreCase) + 3,
                            defaultIv.Length);
                    }

                    continue;
                }

                var value = DecryptAex128Ecb(aesKey, extXKey, cache.Value);
                var (start, end) = GetVideoStartAndEndSize(cache.Key);
                tsList.Add(new OrderByCaches
                {
                    Value = value,
                    Id = start
                });
            }

            tsList = tsList.OrderBy(x => x.Id).ToList();

            var mergeBytes = new List<byte>();
            foreach (var ts in tsList)
            {
                if (isMerge)
                {
                    mergeBytes.AddRange(ts.Value);
                }
                else
                {
                    await File.WriteAllBytesAsync(Path.Combine(saveVideoFilePath, ts.Id + ".ts"), ts.Value);
                }
            }

            if (isMerge)
            {
                var mergeFile = Path.Combine(saveVideoFilePath, Path.GetFileName(decryptVideoFilePath) + ".ts");
                if (File.Exists(mergeFile))
                {
                    File.Delete(mergeFile);
                }

                await using var fileStream = File.Create(mergeFile);
                await fileStream.WriteAsync(mergeBytes.ToArray());
                Console.WriteLine("完成。" + mergeFile);
            }
        }

        /// <summary>
        /// 使用 KEY 解密 AES 数据
        /// </summary>
        /// <param name="keyBytes">Key 的字节数组</param>
        /// <param name="iv">偏移量</param>
        /// <param name="data">待解密的字节数组</param>
        /// <returns>解密成功的数据</returns>
        private static byte[] DecryptAex128Ecb(byte[] keyBytes, string iv, byte[] data)
        {
            using var managed = new AesManaged
            {
                Mode = CipherMode.CBC
            };

            using var decrypt = managed.CreateDecryptor(keyBytes, StrToToHexByte(iv));
            var result = decrypt.TransformFinalBlock(data, 0, data.Length);
            return result;
        }

        /// <summary>
        /// 字符串转16进制字节数组
        /// </summary>
        /// <param name="hexString"></param>
        /// <returns></returns>
        private static byte[] StrToToHexByte(string hexString)
        {
            hexString = hexString.Replace(" ", "").Replace("0x", "");
            if (hexString.Length % 2 != 0)
            {
                hexString += " ";
            }

            var returnBytes = new byte[hexString.Length / 2];
            for (var i = 0; i < returnBytes.Length; i++)
            {
                returnBytes[i] = Convert.ToByte(hexString.Substring(i * 2, 2), 16);
            }

            return returnBytes;
        }

        /// <summary>
        /// 获取视频分段开始和结束大小
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static (int start, int end) GetVideoStartAndEndSize(string url)
        {
            var collection = HttpUtility.ParseQueryString(new Uri(url).Query);

            var start = collection.Get("start");
            var end = collection.Get("end");

            if (start == null || end == null)
            {
                return (0, 0);
            }

            return (int.Parse(start), int.Parse(end));
        }
    }
}