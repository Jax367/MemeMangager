using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing=System.Drawing;

namespace MemeManager {
 public partial class ManagerWindow {
  Window window; Library library; NativeTray tray; HwndSource source; IntPtr handle;
  ListBox navigation,groups; WrapPanel cards; TextBox search; ComboBox sort; TextBlock status;
  string filter="all"; bool rebuilding,importing,exiting,rendering; int rendered,firstRendered=-1; List<Meme> visible=new List<Meme>();
  CancellationTokenSource importCancellation; readonly Dictionary<string,BitmapSource> thumbnails=new Dictionary<string,BitmapSource>();
  readonly List<GifPlayer> players=new List<GifPlayer>(); DispatcherTimer searchTimer,releaseTimer;
  public ManagerWindow(){RenderOptions.ProcessRenderMode=RenderMode.SoftwareOnly;using(var stream=typeof(ManagerWindow).Assembly.GetManifestResourceStream("MemeManager.MainWindow.xaml"))window=(Window)XamlReader.Load(stream);using(var stream=typeof(ManagerWindow).Assembly.GetManifestResourceStream("MemeManager.Icon.png")){var icon=BitmapFrame.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);icon.Freeze();window.Icon=icon;Find<Image>("BrandIcon").Source=icon;}}
  T Find<T>(string name) where T:class{return window.FindName(name) as T;}
  void Click(string name,RoutedEventHandler action){Find<Button>(name).Click+=action;}
  public void Initialize(string[] args){
   Application.Current.MainWindow=window;
   string dataRoot=StoragePaths.Resolve(args);
   library=new Library(dataRoot);
   navigation=Find<ListBox>("Navigation");groups=Find<ListBox>("Groups");cards=Find<WrapPanel>("Cards");search=Find<TextBox>("Search");sort=Find<ComboBox>("Sort");status=Find<TextBlock>("Status");
   ApplyTheme();RefreshNavigation();navigation.SelectionChanged+=(s,e)=>{if(rebuilding||navigation.SelectedItem==null)return;filter=(string)((ListBoxItem)navigation.SelectedItem).Tag;rebuilding=true;groups.SelectedIndex=-1;rebuilding=false;Refresh();};
   groups.SelectionChanged+=(s,e)=>{if(rebuilding||groups.SelectedItem==null)return;filter=(string)((ListBoxItem)groups.SelectedItem).Tag;rebuilding=true;navigation.SelectedIndex=-1;rebuilding=false;Refresh();};
   searchTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(180)};searchTimer.Tick+=(s,e)=>{searchTimer.Stop();Refresh();};
   search.TextChanged+=(s,e)=>{Find<TextBlock>("SearchHint").Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;searchTimer.Stop();searchTimer.Start();};sort.SelectionChanged+=(s,e)=>Refresh();
   Click("ImportClipboard",(s,e)=>AddFromClipboard());Click("ImportFiles",(s,e)=>ChooseFiles());Click("EmptyImport",(s,e)=>ChooseFiles());Click("ImportFolder",(s,e)=>ChooseFolder());Click("AddGroup",(s,e)=>NewGroup());Click("SettingsButton",(s,e)=>Settings());Click("OpenPicker",(s,e)=>ShowPicker());Click("PreprocessButton",(s,e)=>ShowPreprocessor());Click("BatchToggle",(s,e)=>ToggleBatchMode());Click("BatchSelectAll",(s,e)=>{foreach(var item in visible)batchSelected.Add(item.Id);UpdateBatchUI();RedrawBatchCards();});Click("BatchClear",(s,e)=>{batchSelected.Clear();UpdateBatchUI();RedrawBatchCards();});Click("BatchMove",(s,e)=>{try{var target=Find<ComboBox>("BatchGroup").SelectedItem as PickerGroup;if(target!=null)MoveBatch(target.Id);}catch(Exception ex){Report(ex);}});UpdateBatchUI();
   window.Drop+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))BeginImport((string[])e.Data.GetData(DataFormats.FileDrop));};
   window.PreviewDragOver+=(s,e)=>{e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
   window.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){window.Hide();e.Handled=true;}};
   releaseTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};releaseTimer.Tick+=(s,e)=>{releaseTimer.Stop();if(!window.IsVisible&&(picker==null||!picker.IsVisible)&&!importing)GC.Collect(2,GCCollectionMode.Forced,false);};
   window.Closing+=(s,e)=>{if(!exiting){e.Cancel=true;window.Hide();}};window.IsVisibleChanged+=(s,e)=>{UpdateWallpaper();if(!window.IsVisible)ReleaseImages();};
   Find<ScrollViewer>("Scroller").ScrollChanged+=(s,e)=>{if(e.VerticalChange!=0||e.ViewportHeightChange!=0||e.ViewportWidthChange!=0)RenderMore();};
   window.SizeChanged+=(s,e)=>RenderMore();
   handle=new WindowInteropHelper(window).EnsureHandle();source=HwndSource.FromHwnd(handle);source.AddHook(MessageHook);
   bool registered=Native.RegisterHotKey(handle,1,library.Data.Settings.Modifiers|0x4000,(uint)library.Data.Settings.HotKey);
   tray=new NativeTray(handle,ShowPicker,Show,Settings,ShowPreprocessor,Exit);
   library.Data.Settings.AutoStart=Native.IsStartup();if(library.Data.Settings.AutoStart)try{Native.Startup(true,library.Root);}catch{}Refresh();if(!args.Contains("--background"))Show();
   if(!registered&&!args.Contains("--smoke-test")){Show();MessageBox.Show(window,"快捷键被其他程序占用，请在「外观与偏好」中修改。托盘图标仍可打开窗口。","快捷键提示");}
   if(library.LoadWarning!=null){Show();MessageBox.Show(window,library.LoadWarning,"资料库恢复");}
   ProcessPendingImports();
  }
  IntPtr MessageHook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled){if((uint)msg==Native.ImportMessage){ProcessPendingImports();handled=true;return IntPtr.Zero;}if(tray!=null&&tray.Handle(msg,l)){handled=true;return IntPtr.Zero;}if(msg==0x312&&w.ToInt32()==1){TogglePicker();handled=true;}else if((uint)msg==Native.ShowMessage){Show();handled=true;}return IntPtr.Zero;}
  void Show(){releaseTimer.Stop();window.Show();if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;window.UpdateLayout();RenderMore();window.Activate();Native.SetForegroundWindow(handle);search.Focus();}
  void ShowPreprocessor(){try{string executable=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MemePreprocessor.exe");if(!File.Exists(executable))throw new FileNotFoundException("缺少 MemePreprocessor.exe，请保留发行目录内的预处理工具。");string root=library.Root;int trailing=root.Length-root.TrimEnd('\\').Length;string argument="\""+root+new string('\\',trailing)+"\"";using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo{FileName=executable,Arguments="--data-dir "+argument,UseShellExecute=false,CreateNoWindow=true})){} }catch(Exception ex){Report(ex);}}
  void Exit(){if(importing){MessageBox.Show(window,"正在导入，请等待完成或在导入进度窗口中取消。","导入中");return;}exiting=true;releaseTimer.Stop();StopAnimations();Native.UnregisterHotKey(handle,1);tray.Dispose();if(picker!=null)picker.Close();if(settingsWindow!=null)settingsWindow.Close();window.Close();Application.Current.Shutdown();}
  void Save(){library.Save();if(picker!=null&&picker.IsVisible)RefreshPicker();}
  void Report(Exception error){MessageBox.Show(window,error.Message,"操作未完成",MessageBoxButton.OK,MessageBoxImage.Warning);}
  ListBoxItem Nav(string text,string id,int count){var panel=new DockPanel();var badge=new TextBlock {Text=count.ToString(),FontSize=10,Margin=new Thickness(7,0,0,0),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right};badge.SetResourceReference(TextBlock.ForegroundProperty,"SideMuted");DockPanel.SetDock(badge,Dock.Right);panel.Children.Add(badge);var label=new TextBlock{Text=text,TextTrimming=TextTrimming.CharacterEllipsis};label.SetResourceReference(TextBlock.ForegroundProperty,"SideInk");panel.Children.Add(label);return new ListBoxItem{Content=panel,Tag=id};}
  void RefreshNavigation(){rebuilding=true;navigation.Items.Clear();navigation.Items.Add(Nav("▦   全部表情","all",library.Data.Memes.Count));navigation.Items.Add(Nav("♡   我的喜欢","favorites",library.Data.Memes.Count(x=>x.Favorite)));navigation.Items.Add(Nav("◷   最近使用","recent",library.Data.Memes.Count(x=>x.Uses>0)));navigation.Items.Add(Nav("▱   未分组","ungrouped",library.Data.Memes.Count(x=>x.GroupId==null)));
   UpdateBatchUI();groups.Items.Clear();foreach(var group in library.Data.Groups){var captured=group;var item=Nav("▱   "+group.Name,group.Id,library.Data.Memes.Count(x=>x.GroupId==group.Id));var menu=new ContextMenu();AddMenu(menu,"重命名分组",()=>RenameGroup(captured));AddMenu(menu,"删除分组（保留表情）",()=>RemoveGroup(captured));item.ContextMenu=menu;groups.Items.Add(item);}
   foreach(ListBoxItem item in navigation.Items)if((string)item.Tag==filter)navigation.SelectedItem=item;foreach(ListBoxItem item in groups.Items)if((string)item.Tag==filter)groups.SelectedItem=item;rebuilding=false;
  }
  string GroupName(){if(filter=="all")return "全部表情";if(filter=="favorites")return "我的喜欢";if(filter=="recent")return "最近使用";if(filter=="ungrouped")return "未分组";var group=library.Data.Groups.FirstOrDefault(x=>x.Id==filter);return group==null?"全部表情":group.Name;}
  void Refresh(){if(cards==null)return;if(batchSelected.Count>0){var existing=new HashSet<string>(library.Data.Memes.Select(x=>x.Id));batchSelected.RemoveWhere(id=>!existing.Contains(id));}UpdateBatchUI();StopAnimations();cards.Children.Clear();rendered=0;firstRendered=-1;IEnumerable<Meme> items=library.Data.Memes;
   if(filter=="favorites")items=items.Where(x=>x.Favorite);else if(filter=="recent")items=items.Where(x=>x.Uses>0);else if(filter=="ungrouped")items=items.Where(x=>x.GroupId==null);else if(filter!="all")items=items.Where(x=>x.GroupId==filter);
   string query=search.Text.Trim();if(query.Length>0)items=items.Where(x=>x.Name.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0);
   if(filter=="recent"||sort.SelectedIndex==2)items=items.OrderByDescending(x=>x.LastUsed).ThenByDescending(x=>x.Added);else if(sort.SelectedIndex==1)items=items.OrderByDescending(x=>x.Added);else if(sort.SelectedIndex==3)items=items.OrderBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase);else items=items.OrderByDescending(x=>x.Uses).ThenByDescending(x=>x.LastUsed).ThenByDescending(x=>x.Added);
   visible=items.ToList();Find<TextBlock>("PageTitle").Text=GroupName();Find<StackPanel>("Empty").Visibility=visible.Count==0?Visibility.Visible:Visibility.Collapsed;
   Find<TextBlock>("EmptyTitle").Text=query.Length>0?"没有匹配的表情":library.Data.Memes.Count==0?"添加第一张表情":"分组内暂无表情";
   Find<TextBlock>("EmptyHint").Text=query.Length>0?"尝试其他名称或关键词":"拖入图片或文件夹，建立你的本地表情库";
   status.Text=visible.Count+" 张表情  /  共 "+library.Data.Memes.Count+" 张";Find<ScrollViewer>("Scroller").ScrollToTop();RenderMore();
  }
  Brush BrushFor(string key){return (Brush)window.Resources[key];}
  void StopAnimations(){foreach(var player in players)player.Stop();players.Clear();}
  void ReleaseImages(){StopAnimations();cards.Children.Clear();thumbnails.Clear();firstRendered=-1;rendered=0;releaseTimer.Stop();releaseTimer.Start();}
  BitmapSource Thumbnail(Meme meme){
   BitmapSource bitmap;if(thumbnails.TryGetValue(meme.Id,out bitmap))return bitmap;
   try{
    string folder=Path.Combine(library.Root,"thumbnails"),file=Path.Combine(folder,meme.Id+".png");Directory.CreateDirectory(folder);
    if(!File.Exists(file)){
     // A separate small PNG prevents WIC from retaining the original GIF decoder and its frames.
     try{using(var original=Drawing.Image.FromFile(library.FileFor(meme))){double scale=Math.Min(1,192.0/Math.Max(original.Width,original.Height));int w=Math.Max(1,(int)(original.Width*scale)),h=Math.Max(1,(int)(original.Height*scale));using(var resized=new Drawing.Bitmap(w,h))using(var graphics=Drawing.Graphics.FromImage(resized)){graphics.InterpolationMode=Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;graphics.DrawImage(original,0,0,w,h);resized.Save(file,Drawing.Imaging.ImageFormat.Png);}}}
     catch{using(var input=File.OpenRead(library.FileFor(meme))){var sourceImage=new BitmapImage();sourceImage.BeginInit();sourceImage.CacheOption=BitmapCacheOption.OnLoad;sourceImage.DecodePixelWidth=192;sourceImage.StreamSource=input;sourceImage.EndInit();var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(sourceImage));using(var output=File.Create(file))encoder.Save(output);}}
    }
    using(var stream=File.OpenRead(file)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();bitmap=image;}
    if(thumbnails.Count>=48)thumbnails.Clear();thumbnails[meme.Id]=bitmap;return bitmap;
   }catch{return null;}
  }
  void RenderMore(){
   if(rendering||!window.IsVisible||cards==null)return;var scroller=Find<ScrollViewer>("Scroller");
   double width=scroller.ViewportWidth;if(width<=0)width=Math.Max(1,window.ActualWidth-210);int size=library.Data.Settings.CardSize;
   int columns=Math.Max(1,(int)(width/(size+8))),rowHeight=size+32,totalRows=(visible.Count+columns-1)/columns;
   int firstRow=Math.Max(0,(int)(scroller.VerticalOffset/rowHeight)-1),lastRow=Math.Min(totalRows,(int)Math.Ceiling((scroller.VerticalOffset+Math.Max(1,scroller.ViewportHeight))/rowHeight)+1);
   int first=Math.Min(visible.Count,firstRow*columns),end=Math.Min(visible.Count,lastRow*columns);
   if(firstRendered==first&&rendered==end&&cards.Tag is int&&(int)cards.Tag==columns)return;
   rendering=true;try{StopAnimations();cards.Children.Clear();cards.Tag=columns;firstRendered=first;rendered=end;
    if(firstRow>0)cards.Children.Add(new Border{Width=columns*(size+8),Height=firstRow*rowHeight});
    for(int i=first;i<end;i++)cards.Children.Add(Card(visible[i]));
    if(lastRow<totalRows)cards.Children.Add(new Border{Width=columns*(size+8),Height=(totalRows-lastRow)*rowHeight});
   }finally{rendering=false;}
  }
  bool batchMode;readonly HashSet<string> batchSelected=new HashSet<string>();
  void ToggleBatchMode(){batchMode=!batchMode;if(!batchMode)batchSelected.Clear();UpdateBatchUI();RedrawBatchCards();}
  void ToggleBatchItem(string id){if(!batchSelected.Add(id))batchSelected.Remove(id);UpdateBatchUI();RedrawBatchCards();}
  void RedrawBatchCards(){firstRendered=-1;RenderMore();}
  void UpdateBatchUI(){
   Find<Border>("BatchBar").Visibility=batchMode?Visibility.Visible:Visibility.Collapsed;Find<Button>("BatchToggle").Content=batchMode?"完成选择":"批量分组";
   Find<TextBlock>("BatchCount").Text="已选 "+batchSelected.Count+" 张";Find<Button>("BatchMove").IsEnabled=batchSelected.Count>0;
   var combo=Find<ComboBox>("BatchGroup");var prior=combo.SelectedItem as PickerGroup;string id=prior==null?null:prior.Id;
   combo.Items.Clear();combo.Items.Add(new PickerGroup{Id=null,Label="未分组"});foreach(var group in library.Data.Groups)combo.Items.Add(new PickerGroup{Id=group.Id,Label=group.Name});
   combo.SelectedItem=combo.Items.Cast<PickerGroup>().FirstOrDefault(x=>x.Id==id)??combo.Items[0];
  }
  void MoveBatch(string groupId){
   if(groupId!=null&&!library.Data.Groups.Any(x=>x.Id==groupId))throw new InvalidOperationException("目标分组已不存在，请重新选择。");
   var items=library.Data.Memes.Where(x=>batchSelected.Contains(x.Id)).ToList();if(items.Count==0)return;
   var before=items.Select(x=>x.GroupId).ToArray();for(int i=0;i<items.Count;i++)items[i].GroupId=groupId;
   try{Save();}catch{for(int i=0;i<items.Count;i++)items[i].GroupId=before[i];throw;}
   batchSelected.Clear();batchMode=false;UpdateBatchUI();RefreshNavigation();Refresh();status.Text="已将 "+items.Count+" 张表情加入「"+(groupId==null?"未分组":library.Data.Groups.First(x=>x.Id==groupId).Name)+"」";
  }
  UIElement Card(Meme meme,bool quick=false){int size=quick?104:library.Data.Settings.CardSize;var image=new Image {Source=Thumbnail(meme),Stretch=Stretch.Uniform,Margin=new Thickness(7),Height=size-16};var imageGrid=new Grid();imageGrid.Children.Add(image);
   if(meme.Extension==".gif"){var badge=new Border{Background=new SolidColorBrush(Color.FromArgb(225,48,54,64)),CornerRadius=new CornerRadius(3),Padding=new Thickness(4,1,4,1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(6)};badge.Child=new TextBlock{Text="GIF",FontSize=8,Foreground=Brushes.White};imageGrid.Children.Add(badge);}
   var details=new Grid{Margin=new Thickness(8,1,8,8)};details.ColumnDefinitions.Add(new ColumnDefinition());details.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});var name=new TextBlock {Text=meme.Name,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=11,Foreground=BrushFor("Ink")};details.Children.Add(name);var count=new TextBlock {Text=(meme.Favorite?"♥ ":"")+(meme.Uses==0?"":meme.Uses.ToString()),ToolTip="已使用 "+meme.Uses+" 次",FontSize=9,Foreground=BrushFor("Muted"),Margin=new Thickness(5,1,0,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(count,1);details.Children.Add(count);
   var content=new StackPanel();content.Children.Add(imageGrid);content.Children.Add(details);var border=new Border{Background=BrushFor("Surface"),CornerRadius=new CornerRadius(library.Data.Settings.Radius),BorderThickness=new Thickness(1),BorderBrush=BrushFor("Line"),Child=content,Width=size,Height=size+24,Margin=new Thickness(0,0,8,8),Cursor=Cursors.Hand,ToolTip=meme.Name+"\n已使用 "+meme.Uses+" 次 · 点击复制"+(quick?"":" · 右键管理")};
   bool selected=!quick&&batchMode&&batchSelected.Contains(meme.Id);
   if(!quick&&batchMode){var check=new Border{Background=selected?BrushFor("Accent"):BrushFor("Surface"),BorderBrush=BrushFor("Accent"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),Width=21,Height=21,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(6),IsHitTestVisible=false};check.Child=new TextBlock{Text=selected?"✓":"",Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};imageGrid.Children.Add(check);border.BorderBrush=selected?BrushFor("Accent"):BrushFor("Line");border.ToolTip=meme.Name+" · 点击选择或取消";}
   GifPlayer player=null;border.MouseEnter+=(s,e)=>{border.BorderBrush=BrushFor("Accent");if(meme.Extension==".gif"&&image.Source!=null){player=new GifPlayer(image,library.FileFor(meme));players.Add(player);player.Start();}};border.MouseLeave+=(s,e)=>{border.BorderBrush=BrushFor(selected?"Accent":"Line");if(player!=null){player.Stop();players.Remove(player);player=null;}};
   border.MouseLeftButtonUp+=(s,e)=>{if(!quick&&batchMode){ToggleBatchItem(meme.Id);e.Handled=true;}else Copy(meme,quick?picker:window);};if(quick)return border;var menu=new ContextMenu();AddMenu(menu,meme.Favorite?"取消喜欢":"♡  加入喜欢",()=>{meme.Favorite=!meme.Favorite;Save();RefreshNavigation();Refresh();});
   var move=new MenuItem {Header="移动到分组"};var none=new MenuItem{Header="未分组"};none.Click+=(s,e)=>Move(meme,null);move.Items.Add(none);foreach(var group in library.Data.Groups){var captured=group;var item=new MenuItem{Header=group.Name};item.Click+=(s,e)=>Move(meme,captured.Id);move.Items.Add(item);}menu.Items.Add(move);
   AddMenu(menu,"重命名",()=>{string renamed=Prompt("重命名表情","给这张快乐起个名字",meme.Name);if(renamed!=null){meme.Name=renamed;Save();Refresh();}});AddMenu(menu,"在文件夹中显示",()=>System.Diagnostics.Process.Start("explorer.exe","/select,\""+library.FileFor(meme)+"\""));menu.Items.Add(new Separator());AddMenu(menu,"删除表情",()=>Delete(meme));border.ContextMenu=menu;return border;
  }
  void AddMenu(ContextMenu menu,string label,Action action){var item=new MenuItem{Header=label};item.Click+=(s,e)=>{try{action();}catch(Exception ex){Report(ex);}};menu.Items.Add(item);}
  void Move(Meme meme,string id){meme.GroupId=id;Save();RefreshNavigation();Refresh();}
  void Delete(Meme meme){if(MessageBox.Show(window,"将「"+meme.Name+"」从收藏中删除？原始导入文件不受影响。","删除表情",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;library.Data.Memes.Remove(meme);try{Save();}catch{library.Data.Memes.Add(meme);throw;}try{File.Delete(library.FileFor(meme));}catch{}thumbnails.Remove(meme.Id);RefreshNavigation();Refresh();}
  async void Copy(Meme meme,Window origin){try{
   string path=library.FileFor(meme);if(!File.Exists(path))throw new FileNotFoundException("收藏文件不存在，请重新导入。",path);
   // A file drop retains the original animated GIF. Bitmap/PNG give image-aware apps a fallback.
   var data=new DataObject();data.SetFileDropList(new StringCollection{path});data.SetData("Preferred DropEffect",new MemoryStream(new byte[]{1,0,0,0}));
   BitmapFrame bitmap;using(var stream=File.OpenRead(path))bitmap=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);bitmap.Freeze();data.SetImage(bitmap);
   var png=new MemoryStream();var encoder=new PngBitmapEncoder();encoder.Frames.Add(bitmap);encoder.Save(png);png.Position=0;data.SetData("PNG",png);
   Exception failure=null;for(int attempt=0;attempt<4;attempt++){try{Clipboard.SetDataObject(data,true);failure=null;break;}catch(System.Runtime.InteropServices.COMException ex){failure=ex;}await Task.Delay(60);}if(failure!=null)throw failure;
   try{CompleteCopy(meme,origin);}catch(Exception ex){MessageBox.Show(origin,"已复制，但使用记录保存失败："+ex.Message,"保存失败");}
  }catch(Exception ex){MessageBox.Show(origin,ex.Message,"复制失败",MessageBoxButton.OK,MessageBoxImage.Warning);}}
  void CompleteCopy(Meme meme,Window origin){meme.Uses++;meme.LastUsed=DateTime.Now;Save();origin.Hide();RefreshNavigation();Refresh();}
  void ChooseFiles(){var dialog=new Microsoft.Win32.OpenFileDialog{Title="添加表情",Multiselect=true,Filter="图片与动图|*.gif;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|所有文件|*.*"};if(dialog.ShowDialog(window)==true)BeginImport(dialog.FileNames);}
  void ChooseFolder(){try{string path=Native.PickFolder(handle);if(path!=null)BeginImport(new[]{path});}catch(Exception ex){Report(ex);}}
  bool readingClipboard;
  async void AddFromClipboard(){
   if(importing||readingClipboard){status.Text="当前导入尚未完成，请稍候。";return;}
   readingClipboard=true;Find<Button>("ImportClipboard").IsEnabled=false;
   try{
    IDataObject data=null;for(int attempt=0;attempt<4;attempt++){try{data=Clipboard.GetDataObject();break;}catch(System.Runtime.InteropServices.COMException){if(attempt==3)throw;}await Task.Delay(60);}
    using(var prepared=ClipboardImages.Prepare(data,library.Root)){
     if(prepared.Paths.Length==0){status.Text="剪贴板中没有可导入的图片，请先复制图片或图片文件。";return;}
     await ImportPathsAsync(prepared.Paths);
    }
   }catch(Exception ex){MessageBox.Show(window,ex.Message,"无法从剪贴板添加",MessageBoxButton.OK,MessageBoxImage.Warning);}
   finally{readingClipboard=false;Find<Button>("ImportClipboard").IsEnabled=!importing;}
  }
  async void BeginImport(string[] paths){await ImportPathsAsync(paths);}
  async Task ImportPathsAsync(string[] paths){if(importing){MessageBox.Show(window,"当前导入尚未完成，请稍候。","正在导入");return;}importing=true;importCancellation=new CancellationTokenSource();Find<Button>("ImportClipboard").IsEnabled=false;Find<Button>("ImportFiles").IsEnabled=false;Find<Button>("ImportFolder").IsEnabled=false;
   var progress=Dialog("收集快乐",390,210);var panel=new StackPanel{Margin=new Thickness(25)};var message=new TextBlock{Text="正在导入图片，包含子文件夹…",Foreground=BrushFor("Ink"),Margin=new Thickness(0,0,0,20)};panel.Children.Add(message);panel.Children.Add(new ProgressBar{IsIndeterminate=true,Height=5});var cancel=new Button{Content="取消导入",Margin=new Thickness(0,20,0,0)};cancel.Click+=(s,e)=>{importCancellation.Cancel();cancel.IsEnabled=false;message.Text="正在完成当前文件…";};panel.Children.Add(cancel);progress.Content=panel;progress.Closing+=(s,e)=>{if(importing){e.Cancel=true;importCancellation.Cancel();}};progress.Show();
   try{string groupId=library.Data.Groups.Any(x=>x.Id==filter)?filter:null;var known=new HashSet<string>(library.Data.Memes.Select(x=>x.Id));var result=await Task.Run(()=>library.Import(paths,groupId,known,importCancellation.Token,n=>window.Dispatcher.BeginInvoke(new Action(()=>message.Text="已检查 "+n+" 张图片…"))));
    library.Data.Memes.AddRange(result.Items);Save();RefreshNavigation();Refresh();status.Text=string.Format("{0}导入 {1} 张 · 重复 {2} 张 · 无法读取 {3} 张",result.Cancelled?"已取消，已保留 ":"已",result.Items.Count,result.Duplicates,result.Errors);
   }catch(Exception ex){Report(ex);}finally{importing=false;progress.Close();importCancellation.Dispose();Find<Button>("ImportClipboard").IsEnabled=!readingClipboard;Find<Button>("ImportFiles").IsEnabled=true;Find<Button>("ImportFolder").IsEnabled=true;ProcessPendingImports();}}
  void NewGroup(){string name=Prompt("新建分组","把同一种快乐放在一起","");if(name==null)return;var group=new Group{Id=Guid.NewGuid().ToString("N"),Name=name};library.Data.Groups.Add(group);Save();filter=group.Id;RefreshNavigation();Refresh();}
  void RenameGroup(Group group){string name=Prompt("重命名分组","分组名称",group.Name);if(name!=null){group.Name=name;Save();RefreshNavigation();Refresh();}}
  void RemoveGroup(Group group){if(MessageBox.Show(window,"删除「"+group.Name+"」分组？其中的表情将移至未分组。","删除分组",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;foreach(var item in library.Data.Memes.Where(x=>x.GroupId==group.Id))item.GroupId=null;library.Data.Groups.Remove(group);if(filter==group.Id)filter="all";Save();RefreshNavigation();Refresh();}
  Window Dialog(string title,int width,int height,bool owned=true){return new Window{Title=title,Width=width,Height=height,Icon=window.Icon,Owner=owned&&window.IsVisible?window:null,Resources=window.Resources,WindowStartupLocation=owned&&window.IsVisible?WindowStartupLocation.CenterOwner:WindowStartupLocation.CenterScreen,ResizeMode=ResizeMode.NoResize,Background=BrushFor("Canvas"),FontFamily=window.FontFamily,FontSize=13,ShowInTaskbar=!owned};}
  string Prompt(string title,string hint,string initial){var dialog=Dialog(title,410,228);var panel=new StackPanel{Margin=new Thickness(25)};panel.Children.Add(new TextBlock{Text=hint,Foreground=BrushFor("Ink"),Margin=new Thickness(0,0,0,15)});var input=new TextBox{Text=initial,MaxLength=50,Padding=new Thickness(9)};panel.Children.Add(input);var save=new Button{Content="保存",IsDefault=true,Margin=new Thickness(0,20,0,0),Padding=new Thickness(10)};save.Click+=(s,e)=>{if(input.Text.Trim().Length>0)dialog.DialogResult=true;};panel.Children.Add(save);dialog.Content=panel;dialog.Loaded+=(s,e)=>{input.Focus();input.SelectAll();};return dialog.ShowDialog()==true?input.Text.Trim():null;}
  void ApplyTheme(){
   var p=library.Data.Settings;bool harbor=p.Palette=="港湾雾蓝",dark=p.Palette=="深夜墨",warm=p.Palette=="奶油纸";
   SetBrush("Canvas",harbor?"#EFF0F2":dark?"#17211E":warm?"#F7F3EB":"#F5F7F5");
   SetBrush("Surface",harbor?"#FAFAFB":dark?"#23312B":warm?"#FFFCF6":"#FFFFFF");
   SetBrush("Ink",harbor?"#303640":dark?"#E4EDE7":"#202D29");
   SetBrush("Muted",harbor?"#707985":dark?"#9CAFA3":"#809087");
   SetBrush("Line",harbor?"#D9DDE3":dark?"#35453C":warm?"#E8E1D5":"#E6EBE7");
   SetBrush("Tint",harbor?"#DCE3EC":dark?"#2C4035":warm?"#ECE6D8":"#E5F0E9");
   SetBrush("Side",harbor?"#E6E8EC":dark?"#101A15":warm?"#36352B":"#1E3029");
   if(!string.IsNullOrEmpty(p.CanvasColor))SetBrush("Canvas",p.CanvasColor);if(!string.IsNullOrEmpty(p.SurfaceColor))SetBrush("Surface",p.SurfaceColor);if(!string.IsNullOrEmpty(p.InkColor))SetBrush("Ink",p.InkColor);if(!string.IsNullOrEmpty(p.SideColor))SetBrush("Side",p.SideColor);
   var sideColor=((SolidColorBrush)window.Resources["Side"]).Color;bool lightSide=.2126*sideColor.R+.7152*sideColor.G+.0722*sideColor.B>145;
   SetBrush("SideInk",lightSide?"#4A5565":"#E0E5EB");SetBrush("SideMuted",lightSide?"#737D8A":"#A4AEBB");
   try{SetBrush("Accent",p.Accent);}catch{SetBrush("Accent","#627792");}Find<TextBlock>("ShortcutHint").Text=HotkeyText(p.Modifiers,p.HotKey)+" 呼出  ·  Esc 收起";UpdateWallpaper();
  }
  string wallpaperKey;
  void UpdateWallpaper(){
   var p=library.Data.Settings;bool visible=window.IsVisible||(picker!=null&&picker.IsVisible);
   string key=visible?p.BackgroundImage:null;double opacity=Math.Max(0,Math.Min(100,p.BackgroundOpacity))/100.0;
   if(string.IsNullOrEmpty(key)||opacity==0){window.Resources["Wallpaper"]=Brushes.Transparent;wallpaperKey=null;return;}
   string cacheKey=key+":"+opacity;if(wallpaperKey==cacheKey)return;
   window.Resources["Wallpaper"]=Brushes.Transparent;wallpaperKey=null;
   if(!System.Text.RegularExpressions.Regex.IsMatch(key,@"^[0-9a-f]{64}\.png$"))return;
   try{using(var stream=File.OpenRead(Path.Combine(library.Root,"backgrounds",key))){
    var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
    var brush=new ImageBrush(image){Stretch=Stretch.UniformToFill,Opacity=opacity};brush.Freeze();window.Resources["Wallpaper"]=brush;wallpaperKey=cacheKey;
   }}catch{} // Missing or damaged wallpaper must never prevent opening the library.
  }
  void SetBrush(string key,string color){window.Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}
  static string HotkeyText(uint mods,int key){return ((mods&2)!=0?"Ctrl + ":"")+((mods&4)!=0?"Shift + ":"")+((mods&1)!=0?"Alt + ":"")+((mods&8)!=0?"Win + ":"")+(key==32?"Space":KeyInterop.KeyFromVirtualKey(key).ToString());}
  Window settingsWindow;
  void Settings(){if(settingsWindow!=null){if(settingsWindow.WindowState==WindowState.Minimized)settingsWindow.WindowState=WindowState.Normal;settingsWindow.Activate();return;}var p=library.Data.Settings;var dialog=Dialog("外观与偏好",510,650,false);settingsWindow=dialog;dialog.Closed+=(s,e)=>settingsWindow=null;var panel=new StackPanel{Margin=new Thickness(28,20,28,20)};
   Action<string> label=text=>panel.Children.Add(new TextBlock{Text=text,Foreground=BrushFor("Ink"),Margin=new Thickness(0,13,0,8)});
   panel.Children.Add(new TextBlock{Text="外观设置",FontSize=20,Foreground=BrushFor("Ink"),FontWeight=FontWeights.SemiBold});label("主题底色");var palette=new ComboBox{ItemsSource=new[]{"港湾雾蓝","纸间绿","奶油纸","深夜墨"},SelectedItem=p.Palette};panel.Children.Add(palette);
   label("强调色 · 点击选择，或填写 HEX 色值");var colorRow=new DockPanel();var colorButton=new Button{Content="选择颜色",Padding=new Thickness(12,7,12,7),Margin=new Thickness(10,0,0,0)};DockPanel.SetDock(colorButton,Dock.Right);colorRow.Children.Add(colorButton);var accent=new TextBox{Text=p.Accent,Padding=new Thickness(8)};colorRow.Children.Add(accent);panel.Children.Add(colorRow);colorButton.Click+=(s,e)=>{string picked=Native.PickColor(new WindowInteropHelper(dialog).Handle,accent.Text);if(picked!=null)accent.Text=picked;};
   label("自定义底色 · 留空使用主题默认值");
   var customGrid=new Grid();for(int c=0;c<4;c++)customGrid.ColumnDefinitions.Add(new ColumnDefinition());
   string[] customNames={"背景","卡片","文字","侧栏"};string[] customValues={p.CanvasColor,p.SurfaceColor,p.InkColor,p.SideColor};var customInputs=new TextBox[4];
   for(int c=0;c<4;c++){var cell=new StackPanel{Margin=new Thickness(0,0,c==3?0:8,0)};cell.Children.Add(new TextBlock{Text=customNames[c],Foreground=BrushFor("Muted"),FontSize=11,Margin=new Thickness(0,0,0,5)});customInputs[c]=new TextBox{Text=customValues[c]??"",Padding=new Thickness(5),FontSize=11,MaxLength=7,ToolTip="#RRGGBB；留空使用主题默认色"};cell.Children.Add(customInputs[c]);Grid.SetColumn(cell,c);customGrid.Children.Add(cell);}panel.Children.Add(customGrid);
   string background=p.BackgroundImage,selectedBackground=null;
   label("背景图片");var backgroundRow=new DockPanel();var chooseBackground=new Button{Content="选择图片",Padding=new Thickness(10,6,10,6)};var clearBackground=new Button{Content="清除",Margin=new Thickness(8,0,0,0),Padding=new Thickness(10,6,10,6)};DockPanel.SetDock(chooseBackground,Dock.Right);DockPanel.SetDock(clearBackground,Dock.Right);backgroundRow.Children.Add(clearBackground);backgroundRow.Children.Add(chooseBackground);
   var backgroundName=new TextBlock{Text=string.IsNullOrEmpty(background)?"未设置背景":"已设置自定义背景",Foreground=BrushFor("Muted"),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,8,0)};backgroundRow.Children.Add(backgroundName);panel.Children.Add(backgroundRow);
   chooseBackground.Click+=(s,e)=>{var file=new Microsoft.Win32.OpenFileDialog{Title="选择背景图片",Filter="图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff",CheckFileExists=true};if(file.ShowDialog(dialog)==true){selectedBackground=file.FileName;backgroundName.Text=Path.GetFileName(selectedBackground);}};
   clearBackground.Click+=(s,e)=>{background=null;selectedBackground=null;backgroundName.Text="未设置背景";};
   label("背景可见度 · 等比铺满，动图使用静态首帧");var backgroundOpacity=new Slider{Minimum=0,Maximum=100,Value=p.BackgroundOpacity,TickFrequency=5,IsSnapToTickEnabled=true};panel.Children.Add(backgroundOpacity);
   label("卡片尺寸");var size=new Slider{Minimum=120,Maximum=210,Value=p.CardSize,TickFrequency=10,IsSnapToTickEnabled=true};panel.Children.Add(size);label("卡片圆角");var radius=new Slider{Minimum=0,Maximum=26,Value=p.Radius,TickFrequency=2,IsSnapToTickEnabled=true};panel.Children.Add(radius);
   uint mods=p.Modifiers;int key=p.HotKey;label("全局快捷键 · 点击后按下新组合");var hotkey=new TextBox{Text=HotkeyText(mods,key),IsReadOnly=true,Padding=new Thickness(8)};panel.Children.Add(hotkey);hotkey.PreviewKeyDown+=(s,e)=>{e.Handled=true;Key actual=e.Key==Key.System?e.SystemKey:e.Key;if(actual==Key.LeftCtrl||actual==Key.RightCtrl||actual==Key.LeftShift||actual==Key.RightShift||actual==Key.LeftAlt||actual==Key.RightAlt||actual==Key.LWin||actual==Key.RWin)return;var m=Keyboard.Modifiers;if((m&(ModifierKeys.Control|ModifierKeys.Alt|ModifierKeys.Windows))==0){hotkey.Text="请包含 Ctrl、Alt 或 Win";return;}mods=(uint)m;key=KeyInterop.VirtualKeyFromKey(actual);hotkey.Text=HotkeyText(mods,key);};
   var startup=new CheckBox{Content="登录 Windows 后自动启动（收起至托盘）",IsChecked=Native.IsStartup(),Foreground=BrushFor("Ink"),Margin=new Thickness(0,22,0,0)};panel.Children.Add(startup);panel.Children.Add(new TextBlock{Text="所有表情仅存储在本机 · 关闭窗口后留在托盘\nGIF 保留原文件；粘贴动图效果取决于目标软件。",Foreground=BrushFor("Muted"),FontSize=11,Margin=new Thickness(0,15,0,0),TextWrapping=TextWrapping.Wrap});
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,22,0,0)};var reset=new Button{Content="恢复默认",Margin=new Thickness(0,0,10,0),Padding=new Thickness(12,9,12,9)};reset.Click+=(s,e)=>{palette.SelectedIndex=0;accent.Text="#627792";size.Value=136;radius.Value=6;mods=6;key=32;hotkey.Text=HotkeyText(mods,key);};buttons.Children.Add(reset);var apply=new Button{Content="保存偏好",Padding=new Thickness(18,9,18,9),Background=BrushFor("Accent"),Foreground=Brushes.White};buttons.Children.Add(apply);panel.Children.Add(buttons);
   reset.Click+=(s,e)=>{foreach(var input in customInputs)input.Clear();background=null;selectedBackground=null;backgroundName.Text="未设置背景";backgroundOpacity.Value=35;};
   apply.Click+=(s,e)=>{try{string color=accent.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$"))throw new Exception("强调色请使用 #437F65 这样的六位 HEX 色值。");
    foreach(var input in customInputs)if(input.Text.Trim().Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(input.Text.Trim(),"^#[0-9a-fA-F]{6}$"))throw new Exception("自定义颜色请使用 #RRGGBB 格式，或留空。");
    if(selectedBackground!=null){background=library.ImportBackground(selectedBackground);selectedBackground=null;}
    uint previousMods=p.Modifiers;int previousKey=p.HotKey;bool changed=mods!=previousMods||key!=previousKey;
    if(changed){Native.UnregisterHotKey(handle,1);if(!Native.RegisterHotKey(handle,1,mods|0x4000,(uint)key)){Native.RegisterHotKey(handle,1,previousMods|0x4000,(uint)previousKey);throw new Exception("这个快捷键已被占用，请尝试其他组合。");}}
    try{if((startup.IsChecked==true)!=p.AutoStart)Native.Startup(startup.IsChecked==true,library.Root);}catch{if(changed){Native.UnregisterHotKey(handle,1);Native.RegisterHotKey(handle,1,previousMods|0x4000,(uint)previousKey);}throw;}
    p.BackgroundImage=background;p.BackgroundOpacity=(int)backgroundOpacity.Value;p.Palette=(string)palette.SelectedItem;p.Accent=color;p.CanvasColor=customInputs[0].Text.Trim();p.SurfaceColor=customInputs[1].Text.Trim();p.InkColor=customInputs[2].Text.Trim();p.SideColor=customInputs[3].Text.Trim();p.CardSize=(int)size.Value;p.Radius=(int)radius.Value;p.HotKey=key;p.Modifiers=mods;p.AutoStart=startup.IsChecked==true;Save();ApplyTheme();thumbnails.Clear();Refresh();if(picker!=null&&picker.IsVisible)RefreshPicker();dialog.Close();
   }catch(Exception ex){MessageBox.Show(dialog,ex.Message,"无法保存偏好");}};
   dialog.Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};dialog.Show();
  }
 }
 public class GifPlayer {
  readonly Image target;readonly string path;readonly ImageSource original;Drawing.Image gif;Drawing.Bitmap canvas;Drawing.Graphics graphics;DispatcherTimer timer;int index,count;int[] delays;
  public GifPlayer(Image image,string file){target=image;path=file;original=image.Source;}
  public void Start(){try{gif=Drawing.Image.FromFile(path);count=gif.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time);if(count<2){Stop();return;}int width=Math.Min(260,gif.Width),height=Math.Max(1,(int)(gif.Height*(double)width/gif.Width));if(height>350){width=Math.Max(1,width*350/height);height=350;}canvas=new Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);graphics=Drawing.Graphics.FromImage(canvas);graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;delays=new int[count];try{byte[] bytes=gif.GetPropertyItem(0x5100).Value;for(int i=0;i<count;i++)delays[i]=i*4+4<=bytes.Length?Math.Max(20,Math.Min(60000,BitConverter.ToInt32(bytes,i*4)*10)):100;}catch{for(int i=0;i<count;i++)delays[i]=100;}timer=new DispatcherTimer();timer.Tick+=Tick;Render();timer.Interval=TimeSpan.FromMilliseconds(delays[0]);timer.Start();}catch{Stop();}}
  void Tick(object sender,EventArgs e){try{index=(index+1)%count;Render();timer.Interval=TimeSpan.FromMilliseconds(delays[index]);}catch{Stop();}}
  void Render(){gif.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Time,index);graphics.Clear(Drawing.Color.Transparent);graphics.DrawImage(gif,0,0,canvas.Width,canvas.Height);var rect=new Drawing.Rectangle(0,0,canvas.Width,canvas.Height);var data=canvas.LockBits(rect,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);try{var frame=BitmapSource.Create(canvas.Width,canvas.Height,96,96,PixelFormats.Pbgra32,null,data.Scan0,data.Stride*data.Height,data.Stride);frame.Freeze();target.Source=frame;}finally{canvas.UnlockBits(data);}}
  public void Stop(){if(timer!=null){timer.Stop();timer.Tick-=Tick;timer=null;}if(graphics!=null){graphics.Dispose();graphics=null;}if(canvas!=null){canvas.Dispose();canvas=null;}if(gif!=null){gif.Dispose();gif=null;}target.Source=original;}
 }
}
