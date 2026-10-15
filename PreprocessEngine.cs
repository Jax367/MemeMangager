using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MemeManager.Preprocessing {
 public sealed class BatchOptions {
  public string FfmpegPath,OutputDirectory,Format="gif",Resize="contain";
  public bool ResizeEnabled=true,PreserveAnimation=true;public int Width=256,Height=256;
  public void Validate(){if(string.IsNullOrWhiteSpace(FfmpegPath)||!File.Exists(FfmpegPath))throw new ArgumentException("请指定有效的 ffmpeg.exe，或将它加入 PATH。");if(string.IsNullOrWhiteSpace(OutputDirectory))throw new ArgumentException("请选择输出文件夹。");if(!new[]{"gif","png","jpg","webp","bmp","keep"}.Contains(Format))throw new ArgumentException("不支持的输出格式。");if(!new[]{"contain","cover","stretch"}.Contains(Resize))throw new ArgumentException("不支持的尺寸模式。");if(ResizeEnabled&&(Width<1||Height<1||Width>4096||Height>4096))throw new ArgumentException("尺寸须在 1–4096 像素之间。");StoragePaths.RequireNonC(OutputDirectory);}
 }
 public sealed class BatchEntry {public string Source,Output,Status,Detail;}
 public sealed class BatchResult {public int Completed,Skipped,Failed;public bool Cancelled;public List<BatchEntry> Entries=new List<BatchEntry>();public string ReportPath;}
 public sealed class BatchProgress {public int Current,Total;public string Name,Status;}
 public static class BatchProcessor {
  static readonly HashSet<string> extensions=new HashSet<string>(new[]{".png",".apng",".jpg",".jpeg",".webp",".bmp",".tif",".tiff",".gif"},StringComparer.OrdinalIgnoreCase);
  public static string FindFfmpeg(){var directories=new List<string>{AppDomain.CurrentDomain.BaseDirectory,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools")};directories.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));foreach(string directory in directories)try{string file=Path.Combine(directory.Trim().Trim('"'),"ffmpeg.exe");if(File.Exists(file))return Path.GetFullPath(file);}catch{}return "";}
  public static List<string> Collect(IEnumerable<string> paths,bool recursive,CancellationToken token){var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var directories=new Stack<string>();foreach(var input in paths){token.ThrowIfCancellationRequested();string path=Path.GetFullPath(input);if(File.Exists(path)&&extensions.Contains(Path.GetExtension(path)))files.Add(path);else if(Directory.Exists(path))directories.Push(path);}
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);while(directories.Count>0){token.ThrowIfCancellationRequested();string directory=directories.Pop();if(!seen.Add(directory))continue;try{foreach(string file in Directory.GetFiles(directory)){token.ThrowIfCancellationRequested();if(extensions.Contains(Path.GetExtension(file)))files.Add(file);}if(recursive)foreach(string sub in Directory.GetDirectories(directory))if((File.GetAttributes(sub)&FileAttributes.ReparsePoint)==0)directories.Push(sub);}catch(UnauthorizedAccessException){throw new IOException("无法读取目录："+directory);}catch(DirectoryNotFoundException){throw new IOException("目录已不存在："+directory);}}
   return files.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
  }
  public static bool IsGif(string file){if(string.Equals(Path.GetExtension(file),".gif",StringComparison.OrdinalIgnoreCase))return true;using(var stream=File.OpenRead(file)){var bytes=new byte[6];if(stream.Read(bytes,0,6)!=6)return false;string signature=Encoding.ASCII.GetString(bytes);return signature=="GIF87a"||signature=="GIF89a";}}
  public static bool IsAnimated(string file){if(IsGif(file))return true;using(var stream=File.OpenRead(file))using(var reader=new BinaryReader(stream)){
   byte[] header=reader.ReadBytes(12);if(header.Length<12)return false;
   bool png=header[0]==137&&Encoding.ASCII.GetString(header,1,3)=="PNG";
   bool webp=Encoding.ASCII.GetString(header,0,4)=="RIFF"&&Encoding.ASCII.GetString(header,8,4)=="WEBP";
   if(!png&&!webp)return false;stream.Position=png?8:12;
   while(stream.Position+8<=stream.Length){byte[] chunk=reader.ReadBytes(8);uint length=png?((uint)chunk[0]<<24)|((uint)chunk[1]<<16)|((uint)chunk[2]<<8)|chunk[3]:BitConverter.ToUInt32(chunk,4);string name=Encoding.ASCII.GetString(chunk,png?4:0,4);long next=stream.Position+length+(png?4:(length%2));if(next>stream.Length)return false;
    if(png&&name=="acTL"&&length>=8){byte[] frames=reader.ReadBytes(4);return (((uint)frames[0]<<24)|((uint)frames[1]<<16)|((uint)frames[2]<<8)|frames[3])>1;}
    if(png&&name=="IDAT")return false;if(webp&&(name=="ANIM"||name=="ANMF"))return true;if(webp&&name=="VP8X"&&length>=1){int flags=reader.ReadByte();if((flags&2)!=0)return true;}stream.Position=next;
   }return false;
  }}
  static int FinalFrameDelay(string file){try{using(var stream=File.OpenRead(file))using(var reader=new BinaryReader(stream)){byte[] header=reader.ReadBytes(12);if(header.Length<12)return -1;bool png=header[0]==137&&Encoding.ASCII.GetString(header,1,3)=="PNG",webp=Encoding.ASCII.GetString(header,0,4)=="RIFF"&&Encoding.ASCII.GetString(header,8,4)=="WEBP";if(!png&&!webp)return -1;stream.Position=png?8:12;int delay=-1;
    while(stream.Position+8<=stream.Length){byte[] chunk=reader.ReadBytes(8);uint length=png?((uint)chunk[0]<<24)|((uint)chunk[1]<<16)|((uint)chunk[2]<<8)|chunk[3]:BitConverter.ToUInt32(chunk,4);string name=Encoding.ASCII.GetString(chunk,png?4:0,4);long next=stream.Position+length+(png?4:(length%2));if(next>stream.Length)break;
     if(png&&name=="fcTL"&&length>=26){stream.Position+=20;byte[] timing=reader.ReadBytes(4);int numerator=(timing[0]<<8)|timing[1],denominator=(timing[2]<<8)|timing[3];if(denominator==0)denominator=100;delay=Math.Max(1,Math.Min(65535,(int)Math.Round(numerator*100.0/denominator)));}
     if(webp&&name=="ANMF"&&length>=16){stream.Position+=12;byte[] timing=reader.ReadBytes(3);delay=Math.Max(1,Math.Min(65535,(int)Math.Round((timing[0]|(timing[1]<<8)|(timing[2]<<16))/10.0)));}stream.Position=next;
    }return delay;
   }}catch{return -1;}}
  public static string Quote(string value){var text=new StringBuilder("\"");int slashes=0;foreach(char ch in value){if(ch=='\\'){slashes++;continue;}if(ch=='"'){text.Append('\\',slashes*2+1);text.Append('"');slashes=0;continue;}text.Append('\\',slashes);slashes=0;text.Append(ch);}text.Append('\\',slashes*2);return text.Append('"').ToString();}
  static string TargetExtension(string input,BatchOptions options){if(options.Format!="keep")return "."+options.Format;string ext=Path.GetExtension(input).ToLowerInvariant();return ext==".jpeg"?".jpg":ext==".tiff"?".tif":ext==".apng"?".png":ext;}
  public static string Arguments(string input,string output,string extension,BatchOptions options,bool animated=false){
   bool preserve=options.PreserveAnimation&&animated;
   string filter="[0:v:0]format=rgba";
   if(options.ResizeEnabled){string dimensions=options.Width+":"+options.Height;
    if(options.Resize=="contain")filter+=",scale="+dimensions+":force_original_aspect_ratio=decrease:flags=lanczos,pad="+dimensions+":(ow-iw)/2:(oh-ih)/2:color=0x00000000";
    else if(options.Resize=="cover")filter+=",scale="+dimensions+":force_original_aspect_ratio=increase:flags=lanczos,crop="+dimensions;
    else filter+=",scale="+dimensions+":flags=lanczos";
   }
   filter+=",setsar=1";
   bool flatten=extension==".jpg"||extension==".bmp";
   if(extension==".gif")filter+=",split[pixels][palin];[palin]palettegen=reserve_transparent=1:stats_mode=single[palette];[pixels][palette]paletteuse=new=1:dither=bayer:bayer_scale=3:alpha_threshold=128[out]";
   else if(flatten)filter+=",split[fg][base];[base]lutrgb=r=maxval:g=maxval:b=maxval:a=maxval,format=rgba[white];[white][fg]overlay=shortest=1:format=rgb,format=rgb24[out]";else filter+="[out]";
   string codec=extension==".gif"?"-c:v gif -gifflags 0":extension==".jpg"?"-c:v mjpeg -q:v 2 -pix_fmt yuvj444p":extension==".webp"?(preserve?"-c:v libwebp_anim":"-c:v libwebp")+" -lossless 1 -quality 90 -pix_fmt bgra":extension==".bmp"?"-c:v bmp -pix_fmt bgr24":extension==".tif"?"-c:v tiff -pix_fmt rgba":preserve&&extension==".png"?"-c:v apng -pix_fmt rgba":"-c:v png -pix_fmt rgba";
   string mux=extension==".gif"?"-f gif -loop "+(preserve?"0 -final_delay "+FinalFrameDelay(input)+" ":"-1 "):preserve&&extension==".webp"?"-f webp -loop 0 ":preserve&&extension==".png"?"-f apng -plays 0 ":"-f image2 -update 1 ";
   return "-hide_banner -loglevel error -nostdin -n -threads 1 -filter_complex_threads 1 -i "+Quote(input)+" -filter_complex "+Quote(filter)+" -map "+Quote("[out]")+(preserve?" -fps_mode passthrough ":" -frames:v 1 ")+"-an -sn -dn "+codec+" -threads 1 "+mux+Quote(output);
  }
  public static BatchResult Run(IEnumerable<string> sources,BatchOptions options,CancellationToken token,Action<BatchProgress> progress){
   options.Validate();string outputFolder=Path.GetFullPath(options.OutputDirectory);Directory.CreateDirectory(outputFolder);var files=sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();var result=new BatchResult();int current=0;
   foreach(string input in files){if(token.IsCancellationRequested){result.Cancelled=true;break;}var entry=new BatchEntry{Source=input};string temporary=null;
    try{bool originalGif=IsGif(input),animated=IsAnimated(input);string ext=originalGif?".gif":TargetExtension(input,options);
     if(!extensions.Contains(Path.GetExtension(input))||(originalGif&&!options.PreserveAnimation)||(options.PreserveAnimation&&animated&&!new[]{".gif",".webp",".png"}.Contains(ext))){entry.Status="跳过";entry.Detail=animated?"目标格式无法保留动画，请选择 GIF / WebP / PNG；原 GIF 需勾选保留动图":"不支持的格式";result.Skipped++;}
     else {string name=Path.GetFileNameWithoutExtension(input);string target=Path.Combine(outputFolder,name+ext);int suffix=1;while(File.Exists(target)||Directory.Exists(target))target=Path.Combine(outputFolder,name+" ("+(suffix++)+")"+ext);
      temporary=Path.Combine(outputFolder,".shiqu-"+Guid.NewGuid().ToString("N")+ext);
      if(originalGif){File.Copy(input,temporary);entry.Detail="原 GIF 原样保留（不改变尺寸、帧与播放设置）";}else Execute(options.FfmpegPath,Arguments(input,temporary,ext,options,animated),Path.Combine(outputFolder,".work"),token);
      token.ThrowIfCancellationRequested();if(!File.Exists(temporary)||new FileInfo(temporary).Length==0)throw new IOException("FFmpeg 未生成有效文件。");File.Move(temporary,target);temporary=null;entry.Output=target;entry.Status="完成";result.Completed++;
     }
    }catch(OperationCanceledException){entry.Status="取消";entry.Detail="任务已取消";result.Cancelled=true;result.Entries.Add(entry);break;}
    catch(Exception ex){entry.Status="失败";entry.Detail=ex.Message;result.Failed++;}
    finally{if(temporary!=null)try{File.Delete(temporary);}catch{}}
    result.Entries.Add(entry);current++;if(progress!=null)progress(new BatchProgress{Current=current,Total=files.Count,Name=Path.GetFileName(input),Status=entry.Status});
   }
   result.ReportPath=Path.Combine(outputFolder,"处理记录-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".csv");
   var report=new StringBuilder("状态,源文件,输出文件,说明\r\n");foreach(var entry in result.Entries)report.Append(Csv(entry.Status)).Append(',').Append(Csv(entry.Source)).Append(',').Append(Csv(entry.Output)).Append(',').Append(Csv(entry.Detail)).Append("\r\n");File.WriteAllText(result.ReportPath,report.ToString(),new UTF8Encoding(true));return result;
  }
  static string Csv(string value){value=value??"";if(value.Length>0&&"=+-@".IndexOf(value[0])>=0)value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
  static void Execute(string executable,string arguments,string workDirectory,CancellationToken token){
   token.ThrowIfCancellationRequested();var errors=new StringBuilder();using(var process=new Process()){
    process.StartInfo=new ProcessStartInfo{FileName=executable,Arguments=arguments,UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true};
    Directory.CreateDirectory(StoragePaths.RequireNonC(workDirectory));process.StartInfo.WorkingDirectory=workDirectory;foreach(string name in new[]{"TEMP","TMP","TMPDIR"})process.StartInfo.EnvironmentVariables[name]=workDirectory;process.StartInfo.EnvironmentVariables.Remove("FFREPORT");
    process.ErrorDataReceived+=(s,e)=>{if(e.Data==null)return;lock(errors){errors.AppendLine(e.Data);if(errors.Length>8192)errors.Remove(0,errors.Length-8192);}};
    process.Start();process.BeginErrorReadLine();using(token.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}})){
     // One file at a time, no persistent FFmpeg daemon or image preview cache.
     var watch=Stopwatch.StartNew();while(!process.WaitForExit(100)){if(watch.Elapsed>TimeSpan.FromMinutes(3)){try{process.Kill();}catch{}process.WaitForExit();throw new TimeoutException("处理超过 3 分钟，已终止当前文件。");}}
     process.WaitForExit();token.ThrowIfCancellationRequested();if(process.ExitCode!=0){string detail;lock(errors)detail=errors.ToString().Trim();throw new IOException(detail.Length>0?detail:"FFmpeg 退出码："+process.ExitCode);}
    }
   }
  }
 }
}
