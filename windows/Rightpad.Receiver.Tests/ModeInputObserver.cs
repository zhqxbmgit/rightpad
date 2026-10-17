using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace Rightpad.Receiver.Tests;

internal static class ModeInputObserver
{
    [StructLayout(LayoutKind.Sequential)] private struct RawDevice { public ushort Page,Usage;public uint Flags;public IntPtr Window; }
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
    [DllImport("user32.dll")] private static extern uint GetRawInputData(IntPtr raw,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder? data,ref uint size);
    [DllImport("xinput1_4.dll")] private static extern uint XInputGetState(uint index,out XState state);
    [StructLayout(LayoutKind.Sequential)] private struct XState {public uint Packet;public ushort Buttons;public byte LT,RT;public short LX,LY,RX,RY;}
    public static int Run(string directory)
    {
        Exception? error=null;var thread=new Thread(()=> {try {Show(directory);} catch(Exception e){error=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error is not null) throw error;return 0;
    }
    private static void Show(string directory)
    {
        Directory.CreateDirectory(directory);
        using var log=new StreamWriter(Path.Combine(directory,"actual-input.log"),append:true){AutoFlush=true};
        object gate=new();void Record(string text) {lock(gate)log.WriteLine(DateTimeOffset.Now.ToString("O")+" "+text);}
        var window=new Window {Title="Rightpad Mode safe input test",Width=1050,Height=700,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=Brushes.DarkSlateGray};
        var panel=new StackPanel {Margin=new(32)};window.Content=panel;
        panel.Children.Add(new TextBlock {Text="Rightpad 安全输入验证目标\n键盘、鼠标单击与拖动均记录在本地测试日志。",FontSize=26,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap});
        var button=new Button {Content="单击验证区域",Height=120,Margin=new(0,24,0,24),FontSize=28};panel.Children.Add(button);
        button.Click+=(_,_)=>Record("mouse_click");
        var pad=new Border {Height=250,Background=Brushes.SteelBlue,Child=new TextBlock {Text="拖动验证区域",FontSize=28,Foreground=Brushes.White}};panel.Children.Add(pad);
        bool dragging=false;int moves=0;
        pad.MouseLeftButtonDown+=(_,e)=>{dragging=true;moves=0;pad.CaptureMouse();Record("mouse_drag_down "+e.GetPosition(pad));};
        pad.MouseMove+=(_,_)=>{if(dragging && Mouse.LeftButton==MouseButtonState.Pressed)moves++;};
        pad.MouseLeftButtonUp+=(_,_)=>{if(dragging){dragging=false;pad.ReleaseMouseCapture();Record("mouse_drag_up moves="+moves);}};
        HwndSource? source=null;
        window.SourceInitialized+=(_,_)=>{
            source=(HwndSource)PresentationSource.FromVisual(window);
            source.AddHook((IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)=>{
                if(msg!=0xFF)return IntPtr.Zero;
                uint header=(uint)(IntPtr.Size==8?24:16),size=0;GetRawInputData(lParam,0x10000003,IntPtr.Zero,ref size,header);
                var data=Marshal.AllocHGlobal((int)size);
                try {
                    if(GetRawInputData(lParam,0x10000003,data,ref size,header)!=size)return IntPtr.Zero;
                    if(Marshal.ReadInt32(data)==1) {
                        IntPtr device=Marshal.ReadIntPtr(data,8);uint length=0;GetRawInputDeviceInfo(device,0x20000007,null,ref length);
                        var name=new StringBuilder((int)length+1);GetRawInputDeviceInfo(device,0x20000007,name,ref length);
                        int offset=(int)header;ushort scan=(ushort)Marshal.ReadInt16(data,offset),flags=(ushort)Marshal.ReadInt16(data,offset+2),vk=(ushort)Marshal.ReadInt16(data,offset+6);
                        Record($"raw_keyboard scan={scan:X4} flags={flags:X4} vk={vk:X4} device={name}");
                    }
                } finally {Marshal.FreeHGlobal(data);}
                return IntPtr.Zero;
            });
            if(!RegisterRawInputDevices(new[]{new RawDevice {Page=1,Usage=6,Flags=0x100,Window=source.Handle}},1,(uint)Marshal.SizeOf<RawDevice>()))throw new Exception("Raw Input registration failed");
            Record("observer_ready hwnd="+source.Handle);
        };
        using var cancelled=new CancellationTokenSource();
        var poll=Task.Run(async()=>{
            string? previous=null;
            while(!cancelled.IsCancellationRequested) {
                string next=string.Join(";",Enumerable.Range(0,4).Select(i=>XInputGetState((uint)i,out var s)==0?$"slot={i} buttons={s.Buttons:X4} LT={s.LT} RT={s.RT}":$"slot={i} absent"));
                if(next!=previous){Record("xinput "+next);previous=next;}
                try {await Task.Delay(2,cancelled.Token);} catch(OperationCanceledException) {break;}
            }
        });
        var app=new Application();app.Run(window);cancelled.Cancel();poll.GetAwaiter().GetResult();Record("observer_closed");
    }
}
