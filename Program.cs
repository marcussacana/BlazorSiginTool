using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using BrotliStream = BrotliSharpLib.BrotliStream;
using System.Collections.ObjectModel;

namespace BlazorSiginTool
{
    static class Program
    {
        static void Main(string[] args)
        {
            if (args == null || args.Length == 0) {
                Console.WriteLine("Usage:");
                Console.WriteLine("Drag&Drop a blazor app root directory to the exe");
                Console.ReadKey();
                return;
            }
            foreach (var Arg in args) {
                if (Directory.Exists(Arg))
                    Resigin(Arg);
            }

            Console.WriteLine("Press Any Key To Exit");
            Console.ReadKey();
        }

        static void Resigin(string RootDir) {
            RootDir = RootDir.Replace("\\", "/");
            if (!RootDir.EndsWith("/"))
                RootDir += "/";

            Console.WriteLine("Enumerating Files...");
            string[] Files = (from x in Directory.GetFiles(RootDir, "*", SearchOption.AllDirectories) where 
                              !x.Substring(RootDir.Length).StartsWith(".git") &&
                              !x.Substring(RootDir.Length).StartsWith(".vs")
                              select x.Replace("\\", "/")).ToArray();

            var BlazorBootPath = "_framework/blazor.boot.json".Combine(RootDir);
            var BlazorBootJSON = File.ReadAllText(BlazorBootPath);
            BlazorBootInfo Info = JsonConvert.DeserializeObject<BlazorBootInfo>(BlazorBootJSON);


            var BinDir = "_framework/_bin".Combine(RootDir);
            var Dlls = (from x in Files where x.StartsWith(BinDir) && Path.GetExtension(x).ToLowerInvariant() == ".dll" select x).ToArray();

            foreach (var Dll in Dlls) {
                Console.WriteLine("Sigining: " + Path.GetFileName(Dll));
                Info.resources.assembly[Path.GetFileName(Dll)] = GetChecksum(Dll);
            }

            var WASMDir = "_framework/wasm".Combine(RootDir);
            var WASMFiles = (from x in Files where x.StartsWith(WASMDir) && !IsCompressed(x) select x).ToArray();

            foreach (var WASMFile in WASMFiles) {
                Console.WriteLine("Sigining: " + Path.GetFileName(WASMFile));
                Info.resources.runtime[Path.GetFileName(WASMFile)] = GetChecksum(WASMFile);
            }

            BlazorBootJSON = JsonConvert.SerializeObject(Info, Formatting.Indented);
            File.WriteAllText(BlazorBootPath, BlazorBootJSON);

            var ServiceWorkerAssetsPath = "service-worker-assets.js".Combine(RootDir);
            var ServiceWorkerAssets = File.ReadAllText(ServiceWorkerAssetsPath);
            var ServiceWorkerVersion = ServiceWorkerAssets.Substring(ServiceWorkerAssets.IndexOf("\"version\""));
            ServiceWorkerVersion = ServiceWorkerVersion.Substring(ServiceWorkerVersion.IndexOf(" \"") + 2).Split('"').First();


            Console.WriteLine("Generating service-worker-assets.js");
            StringBuilder Builder = new StringBuilder();
            Builder.AppendLine("self.assetsManifest = {");
            Builder.AppendLine("  \"assets\": [");

            var Resources = (from x in Files where !IsCompressed(x) && Path.GetFileName(x).ToLowerInvariant() != "service-worker-assets.js" select x).ToArray();

            foreach (var Resource in Resources) {
                Console.WriteLine("Sigining: " + Path.GetFileName(Resource));
                Builder.AppendLine("    {");
                Builder.AppendLine($"      \"hash\": \"{GetChecksum(Resource)}\",");
                Builder.AppendLine($"      \"url\": \"{Resource.Substring(RootDir.Length)}\"");
                if (Resources.Last() != Resource)
                    Builder.AppendLine("    },");
                else
                    Builder.AppendLine("    }");
            }
            Builder.AppendLine("  ],");
            Builder.AppendLine($"  \"version\": \"{ServiceWorkerVersion}\"");
            Builder.AppendLine("};");

            File.WriteAllText(ServiceWorkerAssetsPath, Builder.ToString());

            var Compresseds = (from x in Files where IsCompressed(x) && File.Exists(Path.GetFileNameWithoutExtension(x).Combine(Path.GetDirectoryName(x))) select x).ToArray();

            foreach (var Compressed in Compresseds) {
                Console.WriteLine("Recompressing: " + Path.GetFileName(Compressed));
                var Ext = Path.GetExtension(Compressed).ToLowerInvariant();
                switch (Ext) {
                    case ".gz":
                        CompressToGzip(Compressed);
                        break;
                    case ".br":
                        CompressToBrotlin(Compressed);
                        break;
                }
            }

            Console.WriteLine("App Resigned!");
        }

        public static void CompressToBrotlin(string Filename) {
            var InputPath = Path.GetFileNameWithoutExtension(Filename).Combine(Path.GetDirectoryName(Filename));
            using (Stream Input = File.Open(InputPath, FileMode.Open))
            using (Stream Output = File.Create(Filename))
            using (BrotliStream Compressor = new BrotliStream(Output, CompressionMode.Compress))
            {
                Compressor.SetQuality(11);
                Input.CopyTo(Compressor);
                Compressor.Flush();
            }
        }
        public static void CompressToGzip(string Filename)
        {
            var InputPath = Path.GetFileNameWithoutExtension(Filename).Combine(Path.GetDirectoryName(Filename));
            using (Stream Input = File.Open(InputPath, FileMode.Open))
            using (Stream Output = File.Create(Filename))
            using (GZipStream Compressor = new GZipStream(Output, CompressionLevel.Optimal))
            {
                Input.CopyTo(Compressor);
                Compressor.Flush();
            }
        }
        public static bool IsCompressed(string File) {
            var Ext = Path.GetExtension(File).ToLowerInvariant();
            if (Ext == ".gz" || Ext == ".br")
                return true;
            return false;
        }

        public static string GetChecksum(string Filename) {
            using Stream Input = File.Open(Filename, FileMode.Open);
            var S256 = Input.SHA256Checksum();
            return "sha256-" + Convert.ToBase64String(S256);
        }
        static byte[] SHA256Checksum(this Stream Stream)
        {
            long OriPos = Stream.Position;
            using SHA256 SHA256 = SHA256.Create();
            var Data = SHA256.ComputeHash(Stream);
            Stream.Position = OriPos;
            return Data;
        }

        public struct BlazorBootInfo {
            public bool cacheBootResources { get; set; }
            public object config { get; set; }
            public bool debugBuild { get; set; }
            public string entryAssembly { get; set; }
            public bool linkerEnabled { get; set; }

            public ResourceInfo resources { get; set; }
        }

        public struct ResourceInfo {

            public Dictionary<string, string> assembly { get; set; }


            public Dictionary<string, string> runtime { get; set; }

            public object satelliteResources { get; set; }
        }

        public struct ServiceWorkerAssetsInfo { 
            public AssetsInfo assets { get; set; }
            public string version { get; set; }
        }

        public struct AssetsInfo
        {
            public string hash { get; set; }
            public string url { get; set; }
        }

        static string Combine(this string File, string RootDir) => Path.Combine(RootDir, File).Replace("\\", "/");
    }
}
