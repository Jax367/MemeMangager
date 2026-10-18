using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MemeManager {
 // Own only our staging directory, never the clipboard's original files or streams.
 public sealed class ClipboardImages : IDisposable {
  public string[] Paths {get;private set;} string staging;
  const int Limit=50*1024*1024;
  ClipboardImages(){Paths=new string[0];}
  public static bool HasImages(IDataObject data){if(data==null)return false;if(data.GetDataPresent(DataFormats.Bitmap,true))return true;foreach(string format in new[]{"GIF","image/gif","PNG","image/png"})if(data.GetDataPresent(format,false))return true;if(data.GetDataPresent(DataFormats.FileDrop)){var files=data.GetData(DataFormats.FileDrop) as string[];if(files!=null)return files.Any(p=>!string.IsNullOrEmpty(p)&&File.Exists(p)&&Library.Supported(p));}return false;}
  public static ClipboardImages Prepare(IDataObject data,string root){
   var result=new ClipboardImages();if(data==null)return result;
   try{
    if(data.GetDataPresent(DataFormats.FileDrop)){
     var files=data.GetData(DataFormats.FileDrop) as string[];
     if(files!=null){result.Paths=files.Where(p=>!string.IsNullOrEmpty(p)&&File.Exists(p)&&Library.Supported(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(result.Paths.Length>0)return result;}
    }
    foreach(string format in new[]{"GIF","image/gif","PNG","image/png"}){
     if(!data.GetDataPresent(format,false))continue;object value=data.GetData(format,false);byte[] bytes=value as byte[];
     if(bytes==null&&value is Stream){var input=(Stream)value;long position=input.CanSeek?input.Position:0;
      try{if(input.CanSeek)input.Position=0;using(var output=new MemoryStream()){var buffer=new byte[8192];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>Limit)throw new IOException("剪贴板图片请小于 50 MB。");output.Write(buffer,0,count);}bytes=output.ToArray();}}
      finally{if(input.CanSeek)input.Position=position;}
     }
     if(bytes==null||bytes.Length==0)continue;if(bytes.Length>Limit)throw new IOException("剪贴板图片请小于 50 MB。");
     bool gif=format.IndexOf("gif",StringComparison.OrdinalIgnoreCase)>=0;result.Stage(root,bytes,gif?".gif":".png");return result;
    }
    var image=data.GetData(DataFormats.Bitmap,true) as BitmapSource;
    if(image!=null){if((long)image.PixelWidth*image.PixelHeight>100000000)throw new IOException("剪贴板图片尺寸过大，请先缩小图片。");using(var output=new MemoryStream()){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));encoder.Save(output);if(output.Length>Limit)throw new IOException("剪贴板图片请小于 50 MB。");result.Stage(root,output.ToArray(),".png");}}
    return result;
   }catch{result.Dispose();throw;}
  }
  void Stage(string root,byte[] bytes,string extension){staging=Path.Combine(StoragePaths.RequireNonC(root),"temp","clipboard-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);string path=Path.Combine(staging,"剪贴板_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+extension);File.WriteAllBytes(path,bytes);Paths=new[]{path};}
  public void Dispose(){if(staging==null)return;try{foreach(var file in Directory.GetFiles(staging))File.Delete(file);Directory.Delete(staging);}catch{}staging=null;}
 }
}
