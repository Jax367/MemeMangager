using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace MemeManager {
 public class Meme { public string Id {get;set;} public string Name {get;set;} public string Extension {get;set;} public string GroupId {get;set;} public bool Favorite {get;set;} public int Uses {get;set;} public DateTime Added {get;set;} public DateTime LastUsed {get;set;} }
 public class Group { public string Id {get;set;} public string Name {get;set;} }
 public class Preferences { public string Palette {get;set;} public string Accent {get;set;} public string CanvasColor {get;set;} public string SurfaceColor {get;set;} public string InkColor {get;set;} public string SideColor {get;set;} public int CardSize {get;set;} public int Radius {get;set;} public int HotKey {get;set;} public uint Modifiers {get;set;} public bool AutoStart {get;set;} public Preferences(){Palette="港湾雾蓝";Accent="#627792";CardSize=136;Radius=6;HotKey=32;Modifiers=6;} }
 public class Database { public List<Meme> Memes {get;set;} public List<Group> Groups {get;set;} public Preferences Settings {get;set;} public Database(){Memes=new List<Meme>();Groups=new List<Group>();Settings=new Preferences();} }
 public class ImportResult { public List<Meme> Items=new List<Meme>(); public int Duplicates; public int Errors; public bool Cancelled; }
 public class Library {
  public readonly string Root; public Database Data; public string LoadWarning;
  static readonly HashSet<string> Extensions = new HashSet<string>(new[]{".gif",".png",".jpg",".jpeg",".webp",".bmp",".tif",".tiff"},StringComparer.OrdinalIgnoreCase);
  public Library(string root){Root=StoragePaths.RequireNonC(root);Directory.CreateDirectory(Path.Combine(Root,"images"));Data=Load();}
  Database Load(){string file=Path.Combine(Root,"library.json");if(!File.Exists(file))return new Database();try{return Deserialize(file);}catch{try{var recovered=Deserialize(file+".bak");LoadWarning="资料库已从备份恢复。";return recovered;}catch{throw new InvalidDataException("资料库与备份无法读取。为保护原数据，程序已停止。请备份后检查："+file);}}}
  Database Deserialize(string file){var result=new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Deserialize<Database>(File.ReadAllText(file));if(result==null||result.Memes==null||result.Groups==null||result.Settings==null)throw new InvalidDataException();
   // Update untouched defaults from the first release; preserve deliberate customization.
   var p=result.Settings;if(p.Palette=="纸间绿"&&p.Accent=="#437F65"&&p.CardSize==158&&p.Radius==12&&string.IsNullOrEmpty(p.CanvasColor)&&string.IsNullOrEmpty(p.SurfaceColor)&&string.IsNullOrEmpty(p.InkColor)&&string.IsNullOrEmpty(p.SideColor)){p.Palette="港湾雾蓝";p.Accent="#627792";p.CardSize=136;p.Radius=6;}return result;}
  public void Save(){string file=Path.Combine(Root,"library.json"),temp=file+".tmp";File.WriteAllText(temp,new JavaScriptSerializer {MaxJsonLength=int.MaxValue}.Serialize(Data));if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);}
  public string FileFor(Meme meme){return Path.Combine(Root,"images",meme.Id+meme.Extension);}
  public static bool Supported(string file){return Extensions.Contains(Path.GetExtension(file));}
  public ImportResult Import(IEnumerable<string> paths,string groupId,HashSet<string> known,System.Threading.CancellationToken token,Action<int> progress){
   var result=new ImportResult();int scanned=0;
   foreach(string input in paths){IEnumerable<string> files;try{files=Directory.Exists(input)?Walk(input):new[]{input};}catch{result.Errors++;continue;}
    foreach(string file in files){if(token.IsCancellationRequested){result.Cancelled=true;return result;}if(!Supported(file))continue;try{
     string hash;using(var sha=SHA256.Create())using(var stream=File.OpenRead(file))hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
     if(known.Contains(hash)){result.Duplicates++;continue;}
     using(var stream=File.OpenRead(file)){var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.DelayCreation,BitmapCacheOption.OnLoad);if(decoder.Frames.Count==0||decoder.Frames[0].PixelWidth==0)throw new InvalidDataException();}
     var item=new Meme{Id=hash,Name=Path.GetFileNameWithoutExtension(file),Extension=Path.GetExtension(file).ToLowerInvariant(),GroupId=groupId,Added=DateTime.Now};
     string target=FileFor(item);if(!File.Exists(target))File.Copy(file,target);known.Add(hash);result.Items.Add(item);
    }catch{result.Errors++;}finally{scanned++;if(scanned%10==0)progress(scanned);}}
   }return result;
  }
  static IEnumerable<string> Walk(string root){var queue=new Stack<string>();queue.Push(root);while(queue.Count>0){string dir=queue.Pop();string[] files,dirs;try{files=Directory.GetFiles(dir);dirs=Directory.GetDirectories(dir);}catch{continue;}foreach(var file in files)yield return file;foreach(var sub in dirs){System.IO.FileAttributes attributes;try{attributes=File.GetAttributes(sub);}catch{continue;}if((attributes&FileAttributes.ReparsePoint)==0)queue.Push(sub);}}}
 }
}
