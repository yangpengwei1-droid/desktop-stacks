using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
class ExportShellImage {
 [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx,cy; }
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size,uint flags,out IntPtr bitmap); }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)] static extern int SHCreateItemFromParsingName(string name,IntPtr bind,ref Guid riid,out IShellItemImageFactory factory);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
 [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int type,width,height,widthBytes;public ushort planes,bitsPixel;public IntPtr bits; }
 [StructLayout(LayoutKind.Sequential)] struct BITMAPINFO { public uint size;public int width,height;public ushort planes,bitCount;public uint compression,sizeImage;public int xppm,yppm;public uint used,important; }
 [DllImport("gdi32.dll")] static extern int GetObject(IntPtr bitmap,int size,out BITMAP value);
 [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc,IntPtr bitmap,uint first,uint lines,[Out] byte[] pixels,ref BITMAPINFO info,uint usage);
 [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
 [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window,IntPtr dc);
 [STAThread] static int Main(string[] args){
  try{
   Guid iid=typeof(IShellItemImageFactory).GUID;IShellItemImageFactory factory;int hr=SHCreateItemFromParsingName(args[0],IntPtr.Zero,ref iid,out factory);Marshal.ThrowExceptionForHR(hr);
   IntPtr bitmap=IntPtr.Zero;
   try{
    uint flags=args.Length>2&&args[2]=="icon"?5u:1u;
    int requested=args.Length>3?int.Parse(args[3]):128;hr=factory.GetImage(new SIZE{cx=requested,cy=requested},flags,out bitmap);Marshal.ThrowExceptionForHR(hr);
    BITMAP native;GetObject(bitmap,Marshal.SizeOf(typeof(BITMAP)),out native);int w=native.width,h=Math.Abs(native.height);var pixels=new byte[w*h*4];
    var info=new BITMAPINFO{size=40,width=w,height=-h,planes=1,bitCount=32,compression=0,sizeImage=(uint)pixels.Length};
    IntPtr dc=GetDC(IntPtr.Zero);try{if(GetDIBits(dc,bitmap,0,(uint)h,pixels,ref info,0)==0)throw new Exception("Cannot read original icon pixels");}finally{ReleaseDC(IntPtr.Zero,dc);}
    bool hasAlpha=false;for(int i=3;i<pixels.Length;i+=4)if(pixels[i]!=0){hasAlpha=true;break;}
    if(!hasAlpha)for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
    var source=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4);source.Freeze();
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));Directory.CreateDirectory(Path.GetDirectoryName(args[1]));using(var file=File.Create(args[1]))encoder.Save(file);
   }finally{if(bitmap!=IntPtr.Zero)DeleteObject(bitmap);Marshal.FinalReleaseComObject(factory);}
   return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
 }
}
