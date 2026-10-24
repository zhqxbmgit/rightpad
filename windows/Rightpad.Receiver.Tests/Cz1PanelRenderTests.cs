using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Rightpad.Receiver.Tests.Program;

namespace Rightpad.Receiver.Tests;

// Offline WPF layout only: no Window.Show, Runtime.Start, sockets or input backend.
internal static class Cz1PanelRenderTests
{
    private sealed class StartupMemory : IStartupValueStore
    {
        public string? Read(string name) => null;
        public void Write(string name,string command) => throw new InvalidOperationException("No startup writes in rendering.");
        public void Delete(string name) => throw new InvalidOperationException("No startup writes in rendering.");
    }
    public static Task Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("/Rightpad.Receiver;component/Resources/DarkTheme.xaml",UriKind.Relative) });
                var store = new RuntimeSettingsStore(RuntimeSettings.Default with {SensitivityX=10,SensitivityY=10});
                var settings = new SettingsViewModel(store,new(Path.Combine(Path.GetTempPath(),"cz1-render-no-save.json")));
                var runtime = new ReceiverRuntime(store,TextWriter.Null,MouseBackend.VirtualHid,
                    new(IPAddress.Loopback,0),rawMouse:false,motionMode:MotionMode.M_R1,useProductMotionSettings:true);
                var model = new MainViewModel(runtime,settings,new(new(new StartupMemory(),"C:\\offline\\Rightpad.Receiver.exe")));
                model.Refresh();
                string directory = Path.Combine(TestPaths.EvidenceDirectory,"panel-preview");
                Directory.CreateDirectory(directory);
                foreach(bool advanced in new[]{false,true})
                {
                    var view = new MotionView { DataContext=model };
                    if(advanced) settings.Cz1.Glide.Text="NaN";
                    view.Measure(new Size(610,double.PositiveInfinity));
                    view.Arrange(new Rect(0,0,610,view.DesiredSize.Height));view.UpdateLayout();
                    foreach(var expander in Descendants(view).OfType<Expander>()) expander.IsExpanded=advanced;
                    view.Measure(new Size(610,double.PositiveInfinity));view.Arrange(new Rect(0,0,610,view.DesiredSize.Height));view.UpdateLayout();
                    foreach(var editor in Descendants(view).OfType<Cz1ParameterEditor>())
                    {
                        if(!editor.IsVisible)continue;
                        var box = Descendants(editor).OfType<TextBox>().Single();
                        Check(box.ActualWidth>=110,"precise keyboard input has sufficient width");
                    }
                    Check(settings.Cz1.Fields.Select(f=>f.Text).Contains(advanced?"NaN":"120000"),"real field draft rendered");
                    var bitmap = new RenderTargetBitmap(610,(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);
                    bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output=File.Create(Path.Combine(directory,advanced?"cz1-advanced-invalid.png":"cz1-default.png"));encoder.Save(output);
                }
                app.Shutdown();
            }
            catch(Exception e){failure=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw new InvalidOperationException("Offline C panel rendering failed.",failure);
        return Task.CompletedTask;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;}
    }
}
