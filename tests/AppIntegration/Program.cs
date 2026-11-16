using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Xml;
using ProjectEye;
using ProjectEye.Core.Service;

internal static class Integration
{
    [STAThread]
    private static int Main()
    {
        int result=1;
        var app=new App();
        app.Dispatcher.BeginInvoke(new Action(async()=>{
            try
            {
                var collection=(ServiceCollection)typeof(App).GetField("serviceCollection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app);
                var config=(ConfigService)collection.GetInstance(typeof(ConfigService).FullName);
                var rest=(RestService)collection.GetInstance(typeof(RestService).FullName);
                Check(config.options.General.WarnTime==1,"legacy settings preserved");
                Check(config.options.Media!=null&&!config.options.Media.AutoPause&&!config.options.Media.AutoResume&&config.options.Media.Logging,"legacy media defaults");
                int starts=0,ends=0,cancels=0;rest.RestStart+=(s,t)=>starts++;rest.RestCompleted+=(s,t)=>ends++;rest.RestCancelled+=(s,t)=>cancels++;
                config.options.General.RestTime=1;rest.Start();var id=rest.CycleId;rest.Start();
                Check(starts==1&&rest.CycleId==id,"duplicate start guarded");
                await Task.Delay(1500);
                Check(ends==1&&!rest.IsResting,"real dispatcher timer completes rest");
                rest.Start();rest.Cancel();rest.Cancel();await Task.Delay(1300);
                Check(starts==2&&ends==1&&cancels==1&&!rest.IsResting,"cancel stops timer once");
                var viewModel=(ProjectEye.ViewModels.OptionsViewModel)typeof(ProjectEye.Core.WindowManager).GetMethod("CreateViewModel",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{"OptionsWindow"});
                var settingsWindow=new ProjectEye.Views.OptionsWindow {DataContext=viewModel};
                // This is an in-process binding test; it does not inject input or inspect desktop UI.
                var pause=(Project1.UI.Controls.Toggle)settingsWindow.FindName("MediaPauseToggle");
                var resume=(Project1.UI.Controls.Toggle)settingsWindow.FindName("MediaResumeToggle");
                var scope=(System.Windows.Controls.ComboBox)settingsWindow.FindName("MediaScopeCombo");
                Check(pause!=null&&resume!=null&&scope!=null,"media controls compiled");
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                System.Windows.Data.BindingOperations.GetBindingExpression(resume,System.Windows.UIElement.IsEnabledProperty).UpdateTarget();
                Console.WriteLine("Binding: status={0}; modelPause={1}; dataContext={2}",System.Windows.Data.BindingOperations.GetBindingExpression(resume,System.Windows.UIElement.IsEnabledProperty).Status,viewModel.Model.MediaAutoPause,resume.DataContext==null?"null":resume.DataContext.GetType().Name);
                Check(!resume.IsEnabled,"resume disabled while pause off");
                viewModel.Model.MediaAutoPause=true;
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                System.Windows.Data.BindingOperations.GetBindingExpression(resume,System.Windows.UIElement.IsEnabledProperty).UpdateTarget();
                Check(resume.IsEnabled,"resume enabled with pause");
                viewModel.Model.MediaAutoResume=true;viewModel.Model.MediaAutoPause=false;
                Check(!viewModel.Model.MediaAutoResume&&!config.options.Media.AutoResume,"disabling pause clears resume");
                settingsWindow.Close();
                config.options.Media.AutoPause=true;config.options.Media.AutoResume=true;config.options.Media.Scope=1;config.options.Media.Logging=false;
                Check(config.Save(),"settings save");
                var xml=new XmlDocument();xml.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data/config.xml"));
                Check(xml.SelectSingleNode("/Options/Media/AutoResume").InnerText=="true"&&xml.SelectSingleNode("/Options/General/WarnTime").InnerText=="1","new settings serialized with legacy values");
                config.Init();
                Check(config.options.Media.AutoPause&&config.options.Media.AutoResume&&config.options.Media.Scope==1&&!config.options.Media.Logging,"configuration reload preserves media values");
                // Reset test-only media config before normal exit; this harness never sends commands.
                config.options.Media.AutoPause=false;config.options.Media.AutoResume=false;config.Save();
                result=0;Console.WriteLine("App integration: PASS");
            }
            catch(Exception error){Console.Error.WriteLine(error);result=1;}
            finally{app.Shutdown(result);}
        }),DispatcherPriority.ApplicationIdle);
        app.Run();return result;
    }
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS "+name);}
}
