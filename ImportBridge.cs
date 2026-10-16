using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MemeManager {
 public sealed class ProcessedImportRequest {public string Id {get;set;}public string[] Files {get;set;}public string GroupId {get;set;}}
 public sealed class ProcessedImportResponse {public int Imported {get;set;}public int Duplicates {get;set;}public int Failed {get;set;}public string Error {get;set;}}
 public static class ImportBridge {
  static string Folder(string root){return Path.Combine(root,"import-requests");}
  static string FileFor(string root,string id,string suffix){Guid value;if(!Guid.TryParseExact(id,"N",out value))throw new ArgumentException("无效导入请求编号。");return Path.Combine(Folder(root),id+suffix);}
  static void Write(string file,object data){string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(data));File.Move(temp,file);}finally{if(File.Exists(temp))File.Delete(temp);}}
  public static string Submit(string root,IEnumerable<string> files,string groupId){string[] paths=files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();if(paths.Length==0||paths.Length>10000)throw new ArgumentException("请选择 1–10000 张已处理图片导入。");Directory.CreateDirectory(Folder(root));string id=Guid.NewGuid().ToString("N");Write(FileFor(root,id,".request.json"),new ProcessedImportRequest{Id=id,Files=paths,GroupId=groupId});return id;}
  public static ProcessedImportRequest Next(string root){if(!Directory.Exists(Folder(root)))return null;foreach(string file in Directory.GetFiles(Folder(root),"*.request.json").OrderBy(File.GetLastWriteTimeUtc))try{
    var request=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<ProcessedImportRequest>(File.ReadAllText(file));if(request==null||request.Files==null||request.Files.Length==0||request.Files.Length>10000||!string.Equals(FileFor(root,request.Id,".request.json"),file,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
    if(File.Exists(FileFor(root,request.Id,".response.json"))){File.Delete(file);continue;}return request;
   }catch{try{File.Move(file,file+".invalid");}catch{}}return null;}
  public static void Complete(string root,string id,ProcessedImportResponse response){string target=FileFor(root,id,".response.json");if(!File.Exists(target))Write(target,response);File.Delete(FileFor(root,id,".request.json"));}
  public static ProcessedImportResponse ReadResponse(string root,string id){string file=FileFor(root,id,".response.json");if(!File.Exists(file))return null;return new JavaScriptSerializer().Deserialize<ProcessedImportResponse>(File.ReadAllText(file));}
  public static void Acknowledge(string root,string id){File.Delete(FileFor(root,id,".response.json"));}
  public static async Task<ProcessedImportResponse> WaitAsync(string root,string id){var until=DateTime.UtcNow.AddMinutes(3);while(DateTime.UtcNow<until){var response=ReadResponse(root,id);if(response!=null){Acknowledge(root,id);return response;}await Task.Delay(150);}throw new TimeoutException("导入尚未返回结果。请求已保存，请确认已打开新版 Manager；正在进行的其他导入结束后会继续处理。");}
 }
 public partial class ManagerWindow {
  bool processingImportRequests;
  void ProcessPendingImports(){window.Dispatcher.BeginInvoke(new Action(ProcessPendingImportsCore));}
  async void ProcessPendingImportsCore(){
   if(processingImportRequests||importing)return;processingImportRequests=true;importing=true;Find<System.Windows.Controls.Button>("ImportClipboard").IsEnabled=false;Find<System.Windows.Controls.Button>("ImportFiles").IsEnabled=false;Find<System.Windows.Controls.Button>("ImportFolder").IsEnabled=false;
   try{ProcessedImportRequest request;while((request=ImportBridge.Next(library.Root))!=null){var response=new ProcessedImportResponse();var added=new List<Meme>();try{
     string group=library.Data.Groups.Any(g=>g.Id==request.GroupId)?request.GroupId:null;
     string[] files=request.Files.Where(p=>!string.IsNullOrWhiteSpace(p)&&Path.IsPathRooted(p)&&!Directory.Exists(p)&&Library.Supported(p)).ToArray();response.Failed=request.Files.Length-files.Length;
     status.Text="正在导入预处理结果…";var known=new HashSet<string>(library.Data.Memes.Select(x=>x.Id));var result=await Task.Run(()=>library.Import(files,group,known,CancellationToken.None,n=>{}));
     if(group!=null&&!library.Data.Groups.Any(g=>g.Id==group))foreach(var item in result.Items)item.GroupId=null;
     added=result.Items;library.Data.Memes.AddRange(added);try{if(added.Count>0)Save();}catch{foreach(var item in added)library.Data.Memes.Remove(item);throw;}
     response.Imported=added.Count;response.Duplicates=result.Duplicates;response.Failed+=result.Errors;
     RefreshNavigation();Refresh();if(picker!=null&&picker.IsVisible)RefreshPicker();status.Text="预处理结果：已导入 "+response.Imported+" 张 · 重复 "+response.Duplicates+" 张 · 失败 "+response.Failed+" 张";
    }catch(Exception ex){response.Error=ex.Message;}ImportBridge.Complete(library.Root,request.Id,response);}
   }catch(Exception ex){status.Text="预处理结果导入失败："+ex.Message;}
   finally{processingImportRequests=false;importing=false;Find<System.Windows.Controls.Button>("ImportClipboard").IsEnabled=!readingClipboard;Find<System.Windows.Controls.Button>("ImportFiles").IsEnabled=true;Find<System.Windows.Controls.Button>("ImportFolder").IsEnabled=true;}
  }
 }
}
