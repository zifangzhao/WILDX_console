using System;
using System.IO;
namespace CE32_console {
 internal static class NpSdGeometry {
  internal static uint U(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
  internal static int Count(uint mask){return (int)((mask&1)+((mask>>1)&1)+((mask>>2)&1));}
  internal static bool Valid(uint mask,uint hz){return mask>=1 && mask<=7 && (hz==7500 || hz==15000 || hz==30000) && !(mask==7 && hz==30000);}
  internal static int Probe(uint mask,uint sequence){uint index=sequence%(uint)Count(mask);for(int p=0;p<3;p++)if((mask&(1U<<p))!=0 && index--==0)return p;throw new InvalidDataException("Empty NP mask");}
  internal static void Validate(byte[] h,byte[] f,long start,long end){
   if(U(h,8)==7||U(h,8)==8)throw new InvalidDataException("This is typed V1 camera/audio data (v7/v8), not NP samples. Use the strict V1 media exporter; NP export was not started.");
   foreach(int p in new[]{8,12,16,20,24,28,32,36,40,44,48,80})if(U(h,p)!=U(f,p))throw new InvalidDataException("NP footer/session mismatch");
   uint version=U(h,8),mask=U(h,16),width=U(h,20),hz=U(h,24),lane=U(h,32),hs=U(h,48),chunks=U(f,68);
   bool legacy=(version==3 || version==4) && U(h,80)==1 && ((mask==1 && width==1)||(mask==2 && width==1)||(mask==3 && width==2)) && (hz==15000 || hz==30000);
   bool paired=(version==5 && mask==7 && hz==15000 || version==6 && (Valid(mask,hz) || (hz==20000 && mask>=1 && mask<=7 && Count(mask)<=2))) && width==1 && U(h,80)==2 && U(h,96)==61632 && U(f,96)==61632;
   if(!(legacy || paired) || (lane!=15360 && !(version==3 && lane==16256)) || U(h,36)!=128 || U(h,40)!=start || end>U(h,44) ||
      (hs!=8192 && hs!=16384) || U(h,28)!=hz/12 || chunks%8!=0 || start+hs+(long)chunks*128>end || !U(h,12).Equals(U(f,12)))
    throw new InvalidDataException("Unsupported/inconsistent NP SD geometry");
  }
  internal static void Descriptor(byte[] h,byte[] d,uint chunk,long lba){
   foreach(int p in new[]{8,12,16,20,24,28,32,36,40,44,48,80,96})if(U(h,p)!=U(d,p))throw new InvalidDataException("NP chunk/session mismatch");
   uint mask=U(h,16);
   if(U(d,52)!=chunk || U(d,56)!=lba || U(d,88)!=Probe(mask,chunk)+1 || U(d,92)!=chunk/Count(mask) ||
      (U(h,8)==6 && U(d,100)!=0x11 && U(d,100)!=0x22))throw new InvalidDataException("NP per-probe chunk order/model mismatch");
  }
 }
}
