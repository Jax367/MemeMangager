using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using ShapePath=System.Windows.Shapes.Path;

namespace MemeManager {
 public partial class ManagerWindow {
  Button pickerPin;
  // WindowChrome keeps native caption dragging, double-click, system menu and edge resizing.
  Button AddWindowTitleBar(Window target,bool quick){
   var content=(UIElement)target.Content;target.Content=null;
   WindowChrome.SetWindowChrome(target,new WindowChrome{CaptionHeight=36,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(0),UseAeroCaptionButtons=false});
   var frame=new Border{BorderThickness=new Thickness(1)};frame.SetResourceReference(Border.BorderBrushProperty,"Line");
   var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=new GridLength(36)});layout.RowDefinitions.Add(new RowDefinition());frame.Child=layout;
   var caption=new Border{BorderThickness=new Thickness(0,0,0,1)};caption.SetResourceReference(Border.BackgroundProperty,"Surface");caption.SetResourceReference(Border.BorderBrushProperty,"Line");layout.Children.Add(caption);
   var bar=new Grid();bar.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42)});bar.ColumnDefinitions.Add(new ColumnDefinition());bar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});caption.Child=bar;
   var icon=new Image{Source=target.Icon,Width=20,Height=20,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};WindowChrome.SetIsHitTestVisibleInChrome(icon,true);icon.MouseLeftButtonDown+=(s,e)=>{if(e.ClickCount==2)target.Close();else SystemCommands.ShowSystemMenu(target,target.PointToScreen(new Point(0,36)));e.Handled=true;};bar.Children.Add(icon);
   var title=new TextBlock{FontSize=11,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,0,12,0)};title.SetBinding(TextBlock.TextProperty,new Binding("Title"){Source=target});title.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Grid.SetColumn(title,1);bar.Children.Add(title);
   var actions=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(actions,2);bar.Children.Add(actions);
   var pin=CaptionButton(target,quick?"PickerPinWindow":"PinWindow","置顶窗口","M 7,1 L 13,1 M 8,1 L 8,6 L 5,9 L 5,11 L 15,11 L 15,9 L 12,6 L 12,1 M 10,11 L 10,17");pin.Width=36;actions.Children.Add(pin);if(!quick)target.RegisterName("PinWindow",pin);
   var separator=new Border{Width=1,Height=14,Margin=new Thickness(4,0,4,0),VerticalAlignment=VerticalAlignment.Center};separator.SetResourceReference(Border.BackgroundProperty,"Line");actions.Children.Add(separator);
   var minimize=CaptionButton(target,"CaptionMinimize","最小化","M 4,10 L 14,10");minimize.Click+=(s,e)=>target.WindowState=WindowState.Minimized;actions.Children.Add(minimize);
   var maximize=CaptionButton(target,"CaptionMaximize","最大化","M 4,4 L 14,4 L 14,14 L 4,14 Z");maximize.Click+=(s,e)=>{target.WindowState=target.WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;};actions.Children.Add(maximize);
   var close=CaptionButton(target,"CaptionClose","关闭","M 4,4 L 14,14 M 14,4 L 4,14");close.Width=46;close.Style=(Style)target.FindResource("CaptionCloseButton");close.Click+=(s,e)=>target.Close();actions.Children.Add(close);
   Grid.SetRow(content,1);layout.Children.Add(content);target.Content=frame;
   target.StateChanged+=(s,e)=>{bool full=target.WindowState==WindowState.Maximized;frame.Padding=full?SystemParameters.WindowResizeBorderThickness:new Thickness(0);maximize.ToolTip=full?"还原":"最大化";AutomationProperties.SetName(maximize,(string)maximize.ToolTip);((ShapePath)maximize.Content).Data=Geometry.Parse(full?"M 6,4 L 6,2 L 16,2 L 16,12 L 14,12 M 3,6 L 12,6 L 12,15 L 3,15 Z":"M 4,4 L 14,4 L 14,14 L 4,14 Z");};
   return pin;
  }
  Button CaptionButton(Window target,string name,string label,string geometry){var button=new Button{Name=name,ToolTip=label,Style=(Style)target.FindResource("CaptionButton")};AutomationProperties.SetName(button,label);WindowChrome.SetIsHitTestVisibleInChrome(button,true);var glyph=new ShapePath{Data=Geometry.Parse(geometry),Width=18,Height=18,StrokeThickness=1.2,StrokeLineJoin=PenLineJoin.Round,SnapsToDevicePixels=true};glyph.SetBinding(ShapePath.StrokeProperty,new Binding("Foreground"){Source=button});button.Content=glyph;return button;}
  void SetPinAppearance(Button button,bool pinned,string label){if(button==null)return;button.ToolTip=pinned?"取消"+label+"置顶":label+"置顶";AutomationProperties.SetName(button,(string)button.ToolTip);AutomationProperties.SetItemStatus(button,pinned?"已置顶":"未置顶");if(pinned){button.SetResourceReference(Button.BackgroundProperty,"Tint");button.SetResourceReference(Button.ForegroundProperty,"Accent");}else{button.ClearValue(Button.BackgroundProperty);button.ClearValue(Button.ForegroundProperty);}((ShapePath)button.Content).RenderTransform=new RotateTransform(pinned?0:35,9,9);}
  void UpdatePinButtons(){SetPinAppearance(Find<Button>("PinWindow"),window.Topmost,"管理窗口");if(pickerPin!=null)SetPinAppearance(pickerPin,picker.Topmost,"选择窗口");}
  void ToggleWindowPin(Window target,bool quick){bool previous=target.Topmost;try{target.Topmost=!previous;if(quick)library.Data.Settings.PickerTopmost=target.Topmost;else library.Data.Settings.ManagerTopmost=target.Topmost;library.Save();}catch(Exception ex){target.Topmost=previous;if(quick)library.Data.Settings.PickerTopmost=previous;else library.Data.Settings.ManagerTopmost=previous;Report(ex);}UpdatePinButtons();}
  void HandlePasteKey(Window owner,KeyEventArgs e){if(e.Key!=Key.V||Keyboard.Modifiers!=ModifierKeys.Control)return;try{var data=Clipboard.GetDataObject();if(!ClipboardImages.HasImages(data))return;e.Handled=true;if(!e.IsRepeat)AddFromClipboard(true,owner,data);}catch(System.Runtime.InteropServices.COMException){e.Handled=true;MessageBox.Show(owner,"剪贴板暂时被其它程序占用，请稍后重试。","暂时无法添加");}catch(Exception ex){e.Handled=true;MessageBox.Show(owner,ex.Message,"无法读取剪贴板");}}
  string ClipboardTarget(Window owner){string id=owner==picker&&pickerGroups!=null&&pickerGroups.SelectedItem!=null?((PickerGroup)pickerGroups.SelectedItem).Id:filter;return library.Data.Groups.Any(x=>x.Id==id)?id:null;}
  Window CreateClipboardConfirmation(ClipboardImages prepared,Window owner,string groupId){var dialog=Dialog("确认添加剪贴板图片",460,420,false);dialog.ShowInTaskbar=false;if(owner!=null&&owner.IsVisible){dialog.Owner=owner;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;}var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="添加剪贴板图片？",FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=BrushFor("Ink")});panel.Children.Add(new TextBlock{Text=prepared.Paths.Length+" 张图片 → "+(groupId==null?"未分组":Grouping.Path(library.Data.Groups,groupId)),Foreground=BrushFor("Muted"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,8,0,12)});
   var preview=new Image{Height=138,Stretch=Stretch.Uniform};try{using(var input=File.OpenRead(prepared.Paths[0])){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=320;image.StreamSource=input;image.EndInit();image.Freeze();preview.Source=image;}}catch{}panel.Children.Add(new Border{Child=preview,Background=BrushFor("Surface"),CornerRadius=new CornerRadius(6),Padding=new Thickness(8)});panel.Children.Add(new TextBlock{Text="原图片文件和 GIF 数据会保留原格式，重复图片会复用并加入目标分组。",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=BrushFor("Muted"),Margin=new Thickness(0,10,0,12)});
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="取消",IsCancel=true,Margin=new Thickness(0,0,8,0)};var confirm=new Button{Content="确定添加",IsDefault=true,Background=BrushFor("Accent"),Foreground=Brushes.White};confirm.Click+=(s,e)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);dialog.Content=panel;dialog.Closed+=(s,e)=>preview.Source=null;return dialog;
  }
 }
}
