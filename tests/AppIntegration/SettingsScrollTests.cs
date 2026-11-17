using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProjectEye.Core.Service;

internal static class SettingsScrollTests
{
    internal static async Task Run(ServiceCollection services,Window window,Action<bool,string> check)
    {
        var theme=(ThemeService)services.GetInstance(typeof(ThemeService).FullName);
        var tabs=Logical<TabControl>(window).First();
        var content=(FrameworkElement)window.Content;
        foreach(string name in new[]{"Blue","Dark"})
        {
            theme.SetTheme(name);
            foreach(double scale in new[]{1.0,1.5,2.0})
            {
                int count=0;
                foreach(TabItem tab in tabs.Items)
                {
                    tabs.SelectedItem=tab;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    content.Measure(new Size(560,330));content.Arrange(new Rect(0,0,560,330));content.UpdateLayout();
                    var viewer=Find<ScrollViewer>(content).FirstOrDefault(v=>v.Style==window.Resources["SettingsScrollViewer"]);
                    if(viewer==null)continue;
                    viewer.ApplyTemplate();viewer.UpdateLayout();
                    var bar=(ScrollBar)viewer.Template.FindName("PART_VerticalScrollBar",viewer);
                    check(bar!=null&&bar.Visibility==Visibility.Visible&&Math.Abs(bar.ActualWidth-12)<0.1&&bar.Opacity>0,"settings "+name+" "+scale+" page "+count+" persistent 12px bar");
                    bar.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
                    viewer.ScrollToEnd();viewer.UpdateLayout();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    check(Math.Abs(viewer.VerticalOffset-viewer.ScrollableHeight)<1,"settings page bottom reachable");
                    viewer.ScrollToHome();viewer.UpdateLayout();
                    if(viewer.ScrollableHeight>0)
                    {
                        viewer.PageDown();viewer.UpdateLayout();
                        check(viewer.VerticalOffset>0,"scrollbar page command changes offset");
                        viewer.ScrollToHome();viewer.UpdateLayout();
                    }
                    else check(!bar.IsEnabled,"empty scroll track is disabled");
                    if(count==0)
                    {
                        var bitmap=new RenderTargetBitmap((int)(560*scale),(int)(330*scale),96*scale,96*scale,PixelFormats.Pbgra32);
                        bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                        using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-"+name+"-"+scale+".png")))png.Save(file);
                    }
                    count++;
                }
                check(count>=6,"all settings scroll pages covered "+name+" "+scale);
            }
        }
        await Task.Delay(1500);
        var bars=Find<ScrollBar>(content).Where(b=>b.Name=="PART_VerticalScrollBar").ToArray();
        check(bars.Length>0&&bars.All(b=>b.Opacity>0&&Math.Abs(b.ActualWidth-12)<0.1),"settings bars remain visible after idle");
        theme.SetTheme("Blue");
    }
    private static IEnumerable<T> Logical<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T)yield return (T)root;
        foreach(object item in LogicalTreeHelper.GetChildren(root))if(item is DependencyObject)
            foreach(var child in Logical<T>((DependencyObject)item))yield return child;
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T)yield return (T)root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
}
