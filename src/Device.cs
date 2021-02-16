//
// Autor: Elvin Deras (e.deras@unitec.edu)
// Bases on HelloFS.cs (Authors: Jonathan Pryor (jonpryor@vt.edu))
//
// SmallFuseFS: Porting the smallfs developed in c/c++ by Ivan Deras to c#/mono
//
//
using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace OperatingSystem{
	public class FuseDevice {
		public static const int SECTOR_SIZE = 512;
		
		
		string devicePath;
		public string DevicePath { get { return devicePath; } set { devicePath = value; } }
		
		FileStream deviceFile;
		private FileStream DeviceFile{ get { return deviceFile; }}

		public FuseDevice (string path)
		{
			DevicePath = path;					
		}
		
		public bool DeviceOpen()
		{
			if (!File.Exists(DevicePath)) return false;
			
			DeviceFile = new FileStream(name, FileMode.Open, FileAccess.ReadWrite);			
			return true;			
		}

		public void DeviceClose()
		{
			DeviceFile.Close();		
			DeviceFile.Flush(true);
		}
		
		public bool DeviceReadSector(byte[] buffer, int sector)
		{
			DeviceFile.Seek(sector*SECTOR_SIZE, SeekOrigin.Begin);
			return (DeviceFile.Read(buffer, 0, SECTOR_SIZE) == SECTOR_SIZE);			
		}
		
		public void DeviceWriteSector(byte[] buffer, int sector)
		{
			DeviceFile.Seek(sector*SECTOR_SIZE, SeekOrigin.Begin);
			DeviceFile.Write(buffer, 0, SECTOR_SIZE);			
		}

		public void DeviceFlush()
		{
			DeviceFile.Flush(true);
			
		}
	}

}

