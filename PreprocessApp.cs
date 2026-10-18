using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace MemeManager.Preprocessing {
 public sealed class PreprocessWindow {
  public readonly Window View;readonly string dataRoot;readonly List<string> inputs=new List<string>();
  CancellationTokenSource operation;bool running,closeRequested,importWaiting,resultsImported;readonly TextBox output,ffmpeg;readonly TextBlock status;readonly ProgressBar progress;
  List<string> completedOutputs=new List<string>();
  class ImportGroupChoice {public string Id,Name;public override string ToString(){return Name;}}
  T Find<T>(string name)where T:class{return View.FindName(name)as T;}
  void Click(string name,RoutedEventHandler action){Find<Button>(name).Click+=action;}
  public PreprocessWindow(string root){
   dataRoot=root;RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;
   using(var stream=typeof(PreprocessWindow).Assembly.GetManifestResourceStream("MemeManager.Preprocess.xaml"))View=(Window)XamlReader.Load(stream);
   using(var stream=typeof(Native).Assembly.GetManifestResourceStream("MemeManager.MainWindow.xaml")){var xml=new XmlDocument{XmlResolver=null};xml.Load(stream);var ns=new XmlNamespaceManager(xml.NameTable);ns.AddNamespace("w","http://schemas.microsoft.com/winfx/2006/xaml/presentation");var resources=xml.SelectSingleNode("/w:Window/w:Window.Resources",ns);View.Resources=(ResourceDictionary)XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"+resources.InnerXml+"</ResourceDictionary>");}
   using(var stream=typeof(Native).Assembly.GetManifestResourceStream("MemeManager.Icon.png")){var icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);icon.Freeze();View.Icon=icon;}
   ApplyTheme();output=Find<TextBox>("Output");ffmpeg=Find<TextBox>("Ffmpeg");status=Find<TextBlock>("Status");progress=Find<ProgressBar>("Progress");Find<ListBox>("Queue").ItemsSource=inputs;
   output.Text=StoragePaths.ExportDirectory(dataRoot);ffmpeg.Text=BatchProcessor.FindFfmpeg();
   try{string saved=File.ReadAllText(Path.Combine(dataRoot,"ffmpeg-path.txt")).Trim();if(File.Exists(saved))ffmpeg.Text=saved;}catch{}
   Click("AddFiles",(s,e)=>{var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择待处理图片",Multiselect=true,Filter="图片|*.png;*.apng;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff;*.gif|所有文件|*.*"};if(dialog.ShowDialog(View)==true)AddPaths(dialog.FileNames);});
   Click("AddFolder",(s,e)=>{try{string selected=Native.PickFolder(new WindowInteropHelper(View).Handle);if(selected!=null)AddPaths(new[]{selected});}catch(Exception ex){Error(ex);}});
   Click("Clear",(s,e)=>{inputs.Clear();RefreshQueue();});
   Click("ChooseOutput",(s,e)=>{try{string selected=Native.PickFolder(new WindowInteropHelper(View).Handle);if(selected!=null)output.Text=selected;}catch(Exception ex){Error(ex);}});
   Click("ChooseFfmpeg",(s,e)=>{var dialog=new Microsoft.Win32.OpenFileDialog{Title="选择 ffmpeg.exe",Filter="FFmpeg|ffmpeg.exe|可执行文件|*.exe"};if(dialog.ShowDialog(View)==true)ffmpeg.Text=dialog.FileName;});
   Click("Start",(s,e)=>Start());Click("Cancel",(s,e)=>Cancel());Click("ImportResults",(s,e)=>ImportResults());RefreshImportGroups();Click("OpenOutput",(s,e)=>{try{if(!Directory.Exists(output.Text))throw new DirectoryNotFoundException("输出目录尚不存在，请先处理图片。");Process.Start("explorer.exe",BatchProcessor.Quote(Path.GetFullPath(output.Text)));}catch(Exception ex){Error(ex);}});
   Find<CheckBox>("ResizeEnabled").Checked+=(s,e)=>Find<Grid>("ResizeOptions").IsEnabled=true;Find<CheckBox>("ResizeEnabled").Unchecked+=(s,e)=>Find<Grid>("ResizeOptions").IsEnabled=false;
   View.Drop+=(s,e)=>{if(!running&&e.Data.GetDataPresent(DataFormats.FileDrop))AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));};View.PreviewDragOver+=(s,e)=>{e.Effects=!running&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
   View.Closing+=(s,e)=>{if(running&&!importWaiting){closeRequested=true;e.Cancel=true;Cancel();}};
  }
  void ApplyTheme(){try{string file=Path.Combine(dataRoot,"library.json");if(!File.Exists(file))return;var data=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));var settings=(Dictionary<string,object>)data["Settings"];string palette=Convert.ToString(settings["Palette"]);
    string[] keys={"Canvas","Surface","Ink","Muted","Line","Tint"};string[] colors=palette=="深夜墨"?new[]{"#17211E","#23312B","#E4EDE7","#9CAFA3","#35453C","#2C4035"}:palette=="奶油纸"?new[]{"#F7F3EB","#FFFCF6","#202D29","#809087","#E8E1D5","#ECE6D8"}:palette=="纸间绿"?new[]{"#F5F7F5","#FFFFFF","#202D29","#809087","#E6EBE7","#E5F0E9"}:null;if(colors!=null)for(int i=0;i<keys.Length;i++)SetBrush(keys[i],colors[i]);
    foreach(string key in new[]{"Canvas","Surface","Ink"})if(settings.ContainsKey(key+"Color")&&!string.IsNullOrWhiteSpace(Convert.ToString(settings[key+"Color"])))SetBrush(key,Convert.ToString(settings[key+"Color"]));if(settings.ContainsKey("Accent"))SetBrush("Accent",Convert.ToString(settings["Accent"]));
   }catch{}}
  void SetBrush(string key,string value){View.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));}
  void Error(Exception ex){MessageBox.Show(View,ex.Message,"图片预处理",MessageBoxButton.OK,MessageBoxImage.Warning);}
  void RefreshQueue(){Find<ListBox>("Queue").Items.Refresh();Find<TextBlock>("QueueCount").Text=inputs.Count+" 个文件";}
  void Busy(bool busy){running=busy;Find<StackPanel>("Configuration").IsEnabled=!busy;Find<Button>("Start").IsEnabled=!busy;Find<Button>("Cancel").IsEnabled=busy&&!importWaiting;Find<Button>("ImportResults").IsEnabled=!busy&&!resultsImported&&completedOutputs.Count>0;Find<ComboBox>("ImportGroup").IsEnabled=!busy;}
  void Cancel(){if(operation!=null)operation.Cancel();Find<Button>("Cancel").IsEnabled=false;status.Text="正在取消并清理临时文件…";}
  void Finish(){Busy(false);progress.IsIndeterminate=false;operation.Dispose();operation=null;if(closeRequested)View.Close();}
  async void AddPaths(string[] paths){if(running)return;Busy(true);operation=new CancellationTokenSource();progress.IsIndeterminate=true;status.Text="正在收集文件路径，不加载缩略图…";bool recursive=Find<CheckBox>("Recursive").IsChecked==true;
   try{var files=await Task.Run(()=>BatchProcessor.Collect(paths,recursive,operation.Token));var existing=new HashSet<string>(inputs,StringComparer.OrdinalIgnoreCase);foreach(string file in files)if(existing.Add(file))inputs.Add(file);RefreshQueue();status.Text="已添加待处理图片，默认保留动画；原 GIF 将原样保留。";}
   catch(OperationCanceledException){status.Text="已取消添加。";}catch(Exception ex){Error(ex);}finally{Finish();}}
  async void Start(){if(running)return;
   try{
    if(inputs.Count==0)throw new ArgumentException("请先添加图片或文件夹。");bool resize=Find<CheckBox>("ResizeEnabled").IsChecked==true;int width=256,height=256;if(resize&&(!int.TryParse(Find<TextBox>("Width").Text,out width)||!int.TryParse(Find<TextBox>("Height").Text,out height)))throw new ArgumentException("宽、高请填写整数像素。");
    string[] formats={"gif","png","jpg","webp","bmp","keep"},fits={"contain","cover","stretch"};var options=new BatchOptions{FfmpegPath=ffmpeg.Text.Trim().Trim('"'),OutputDirectory=output.Text.Trim().Trim('"'),Format=formats[Find<ComboBox>("Format").SelectedIndex],ResizeEnabled=resize,PreserveAnimation=Find<CheckBox>("PreserveAnimation").IsChecked==true,Width=width,Height=height,Resize=fits[Find<ComboBox>("Fit").SelectedIndex]};options.Validate();
    Directory.CreateDirectory(dataRoot);File.WriteAllText(Path.Combine(dataRoot,"ffmpeg-path.txt"),options.FfmpegPath);
    completedOutputs.Clear();resultsImported=false;Find<Button>("ImportResults").Content="一键导入收藏库";Busy(true);operation=new CancellationTokenSource();progress.IsIndeterminate=false;progress.Value=0;progress.Maximum=inputs.Count;status.Text="开始处理，每次仅启动一个 FFmpeg 任务…";var snapshot=inputs.ToArray();
    try{var reporter=new Progress<BatchProgress>(p=>{progress.Value=p.Current;status.Text=p.Current+" / "+p.Total+"  ·  "+p.Status+"  ·  "+p.Name;});var result=await Task.Run(()=>BatchProcessor.Run(snapshot,options,operation.Token,p=>((IProgress<BatchProgress>)reporter).Report(p)));completedOutputs=result.Entries.Where(x=>x.Status=="完成"&&!string.IsNullOrEmpty(x.Output)).Select(x=>x.Output).ToList();RefreshImportGroups();status.Text=(result.Cancelled?"已取消。":"已完成。")+"成功 "+result.Completed+" · 跳过 "+result.Skipped+" · 失败 "+result.Failed+"。可一键导入已完成的结果。";}
    catch(Exception ex){status.Text="处理失败："+ex.Message;if(!closeRequested)Error(ex);}finally{Finish();}
   }catch(Exception ex){Error(ex);}
  }
  void RefreshImportGroups(){var combo=Find<ComboBox>("ImportGroup");string selected=combo.SelectedItem==null?null:((ImportGroupChoice)combo.SelectedItem).Id;combo.Items.Clear();combo.Items.Add(new ImportGroupChoice{Name="未分组"});try{string file=Path.Combine(dataRoot,"library.json");if(File.Exists(file)){var db=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Database>(File.ReadAllText(file));Grouping.Normalize(db);foreach(var row in Grouping.Rows(db.Groups))combo.Items.Add(new ImportGroupChoice{Id=row.Group.Id,Name=row.Path});}}catch{}combo.SelectedItem=combo.Items.Cast<ImportGroupChoice>().FirstOrDefault(x=>x.Id==selected)??combo.Items[0];}
  async void ImportResults(){if(running||completedOutputs.Count==0||resultsImported)return;string id=null;try{
   string manager=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MemeManager.exe");if(!File.Exists(manager))throw new FileNotFoundException("缺少 MemeManager.exe，请保留完整发行目录。");string group=((ImportGroupChoice)Find<ComboBox>("ImportGroup").SelectedItem).Id;id=ImportBridge.Submit(dataRoot,completedOutputs,group);importWaiting=true;Busy(true);progress.IsIndeterminate=true;status.Text="正在导入本次处理结果，重复图片会复用并关联到目标分组…";
   using(var child=Process.Start(new ProcessStartInfo{FileName=manager,Arguments="--background --import-pending --data-dir "+BatchProcessor.Quote(dataRoot),UseShellExecute=false,CreateNoWindow=true})){}
   var response=await ImportBridge.WaitAsync(dataRoot,id);if(!string.IsNullOrEmpty(response.Error))throw new IOException(response.Error);resultsImported=response.Failed==0;Find<Button>("ImportResults").Content=resultsImported?"已导入收藏库":"重试导入未完成项";status.Text="导入完成：新增 "+response.Imported+" · 复用 "+response.Duplicates+" · 失败 "+response.Failed+"。";
  }catch(Exception ex){status.Text="导入未完成："+ex.Message;Error(ex);}finally{importWaiting=false;Busy(false);progress.IsIndeterminate=false;}}
 }
 public static class PreprocessProgram {
  [STAThread] public static void Main(string[] args){bool first;uint show=Native.RegisterWindowMessage("MemeManager.Preprocess.Show.v1");using(var mutex=new Mutex(true,"Local\\MemeManager.Preprocess.v1",out first)){if(!first){Native.PostMessage(new IntPtr(0xffff),show,IntPtr.Zero,IntPtr.Zero);return;}try{
   string root=StoragePaths.Initialize(args);var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};var ui=new PreprocessWindow(root);app.MainWindow=ui.View;
   var handle=new WindowInteropHelper(ui.View).EnsureHandle();HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled){if((uint)message==show){ui.View.Show();if(ui.View.WindowState==WindowState.Minimized)ui.View.WindowState=WindowState.Normal;ui.View.Activate();Native.SetForegroundWindow(handle);handled=true;}return IntPtr.Zero;});app.Run(ui.View);
  }catch(Exception ex){MessageBox.Show(ex.Message,"预处理工具启动失败",MessageBoxButton.OK,MessageBoxImage.Error);}}}
 }
}
