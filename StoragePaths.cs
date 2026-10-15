using System;
using System.IO;
using System.Linq;

namespace MemeManager {
 public static class StoragePaths {
  public static bool IsOnC(string path){return string.Equals(Path.GetPathRoot(Path.GetFullPath(path)),"C:\\",StringComparison.OrdinalIgnoreCase);}
  public static string RequireNonC(string path){string resolved=Path.GetFullPath(path);if(IsOnC(resolved))throw new IOException("请选择 C 盘以外的位置："+resolved);return resolved;}
  public static string Resolve(string[] args){int index=Array.IndexOf(args,"--data-dir");if(index>=0){if(index+1>=args.Length)throw new ArgumentException("--data-dir 后需要填写目录。");return RequireNonC(args[index+1]);}
   string configuration=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"storage-path.txt");if(File.Exists(configuration)){string configured=File.ReadAllText(configuration).Trim();if(configured.Length==0)throw new IOException("storage-path.txt 中的数据目录为空。");return RequireNonC(Path.IsPathRooted(configured)?configured:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,configured));}
   if(!IsOnC(AppDomain.CurrentDomain.BaseDirectory))return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data");
   var drive=DriveInfo.GetDrives().FirstOrDefault(d=>{try{return d.IsReady&&d.DriveType==DriveType.Fixed&&!IsOnC(d.RootDirectory.FullName);}catch{return false;}});
   if(drive==null)throw new IOException("未找到可用的非 C 盘，请指定其它磁盘的数据目录。");return Path.Combine(drive.RootDirectory.FullName,"MemeManager","data");
  }
  public static string Initialize(string[] args){string root=Resolve(args);Directory.CreateDirectory(root);string temporary=Path.Combine(root,"temp");Directory.CreateDirectory(temporary);Environment.SetEnvironmentVariable("TEMP",temporary);Environment.SetEnvironmentVariable("TMP",temporary);Environment.SetEnvironmentVariable("TMPDIR",temporary);Environment.SetEnvironmentVariable("FFREPORT",null);Environment.CurrentDirectory=root;return root;}
  public static string ExportDirectory(string root){return Path.Combine(RequireNonC(root),"processed",DateTime.Now.ToString("yyyyMMdd-HHmmss"));}
  public static string QuoteArgument(string value){return "\""+value+new string('\\',value.Length-value.TrimEnd('\\').Length)+"\"";}
 }
}
