using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MemeManager {
 public partial class ManagerWindow {
  Button pickerPin;
  void UpdatePinButtons(){var main=Find<Button>("PinWindow");if(main!=null){main.Content=window.Topmost?"取消置顶":"置顶";main.ToolTip=window.Topmost?"管理窗口已置顶":"让管理窗口显示在其它窗口上方";main.SetResourceReference(Button.BackgroundProperty,window.Topmost?"Tint":"Surface");}if(pickerPin!=null){pickerPin.Content=picker.Topmost?"取消置顶":"置顶";pickerPin.SetResourceReference(Button.BackgroundProperty,picker.Topmost?"Tint":"Surface");}}
  void ToggleWindowPin(Window target,bool quick){bool previous=target.Topmost;try{target.Topmost=!previous;if(quick)library.Data.Settings.PickerTopmost=target.Topmost;else library.Data.Settings.ManagerTopmost=target.Topmost;library.Save();}catch(Exception ex){target.Topmost=previous;if(quick)library.Data.Settings.PickerTopmost=previous;else library.Data.Settings.ManagerTopmost=previous;Report(ex);}UpdatePinButtons();}
  void HandlePasteKey(Window owner,KeyEventArgs e){if(e.Key!=Key.V||Keyboard.Modifiers!=ModifierKeys.Control)return;try{var data=Clipboard.GetDataObject();if(!ClipboardImages.HasImages(data))return;e.Handled=true;if(!e.IsRepeat)AddFromClipboard(true,owner,data);}catch(System.Runtime.InteropServices.COMException){e.Handled=true;MessageBox.Show(owner,"剪贴板暂时被其它程序占用，请稍后重试。","暂时无法添加");}catch(Exception ex){e.Handled=true;MessageBox.Show(owner,ex.Message,"无法读取剪贴板");}}
  string ClipboardTarget(Window owner){string id=owner==picker&&pickerGroups!=null&&pickerGroups.SelectedItem!=null?((PickerGroup)pickerGroups.SelectedItem).Id:filter;return library.Data.Groups.Any(x=>x.Id==id)?id:null;}
  Window CreateClipboardConfirmation(ClipboardImages prepared,Window owner,string groupId){var dialog=Dialog("确认添加剪贴板图片",460,420,false);dialog.ShowInTaskbar=false;if(owner!=null&&owner.IsVisible){dialog.Owner=owner;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;}var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="添加剪贴板图片？",FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=BrushFor("Ink")});panel.Children.Add(new TextBlock{Text=prepared.Paths.Length+" 张图片 → "+(groupId==null?"未分组":Grouping.Path(library.Data.Groups,groupId)),Foreground=BrushFor("Muted"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,8,0,12)});
   var preview=new Image{Height=138,Stretch=Stretch.Uniform};try{using(var input=File.OpenRead(prepared.Paths[0])){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=320;image.StreamSource=input;image.EndInit();image.Freeze();preview.Source=image;}}catch{}panel.Children.Add(new Border{Child=preview,Background=BrushFor("Surface"),CornerRadius=new CornerRadius(6),Padding=new Thickness(8)});panel.Children.Add(new TextBlock{Text="原图片文件和 GIF 数据会保留原格式，重复图片会复用并加入目标分组。",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=BrushFor("Muted"),Margin=new Thickness(0,10,0,12)});
   var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="取消",IsCancel=true,Margin=new Thickness(0,0,8,0)};var confirm=new Button{Content="确定添加",IsDefault=true,Background=BrushFor("Accent"),Foreground=Brushes.White};confirm.Click+=(s,e)=>dialog.DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(confirm);panel.Children.Add(buttons);dialog.Content=panel;dialog.Closed+=(s,e)=>preview.Source=null;return dialog;
  }
 }
}
