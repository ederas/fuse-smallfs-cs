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
using Mono.Fuse;
using Mono.Unix.Native;



namespace OperatingSystem{
	
	class FuseSmallFS : FileSystem {
		
		const int MAX_FILE_SIZE         = (MAX_SECTORS_PER_FILE * FuseDevice.SECTOR_SIZE);
		const int MAX_MAP_ENTRIES       = 256;
		const int MAX_ROOT_DENTRIES     = 16;
		const int MAX_NAME_LEN			= 6;
		const int MAX_SECTORS_PER_FILE	= 26;
		const int ENTRY_SIZE = MAX_NAME_LEN + MAX_SECTORS_PER_FILE;
		
		
		Dictionary<string, byte[]> rootDir = new Dictionary<string, byte[]>();
		
		string deviceFile = "";
		public string DeviceFile { get{ return deviceFile; } set{ deviceFile = value; } }

		FuseDevice fsDevice = null;
		byte[] mapDevice = new byte[FuseDevice.SECTOR_SIZE];		
		byte[] dirDevice = new byte[FuseDevice.SECTOR_SIZE];		
		
		static readonly byte[] hello_str = Encoding.UTF8.GetBytes ("Hello World!\n");
		const string hello_path = "/hello";
		const string data_path  = "/data";
		const string data_im_path  = "/data.im";

		const int data_size = 100000000;

		byte[] data_im_str;
		bool have_data_im = false;
		object data_im_str_lock = new object ();
		Dictionary<string, byte[]> hello_attrs = new Dictionary<string, byte[]>();
		


		public FuseSmallFS ()
		{
			Console.WriteLine ("(FuseSmallFS creating)");
			hello_attrs ["foo"] = Encoding.UTF8.GetBytes ("bar");
		}


		private void SerializeDirectoryDevice()
		{
			Console.WriteLine("SerializeDirectoryDevice");
			byte[] entry = new byte[ENTRY_SIZE];
			byte[] entryName = new byte[MAX_NAME_LEN];
			byte[] entrySectors = new byte[MAX_SECTORS_PER_FILE];
			
			string name = "";
			
			for(int i=0; i<FuseDevice.SECTOR_SIZE;i+=ENTRY_SIZE)
			{
				entry[0] = 0x0;
				entryName[0] = 0x0;
				entrySectors[0] = 0x0;
				
				Array.Copy(dirDevice,i,entry,0,ENTRY_SIZE);				
				if (entry[0] == 0x0) continue;
				
				Array.Copy(entry, 0, entryName, 0, MAX_NAME_LEN);
				Array.Copy(entry, MAX_NAME_LEN, entrySectors, 0, MAX_SECTORS_PER_FILE);
				
				name = Encoding.UTF8.GetString(entryName);
				Console.WriteLine("Entry Name: {0}", name);
				rootDir.Add(name, entrySectors);								
			}
			
		}
		protected override Errno OnGetPathStatus (string path, out Stat stbuf)
		{
			Console.WriteLine ("(OnGetPathStatus {0})", path);

			stbuf = new Stat ();
			switch (path) {
				case "/":
					stbuf.st_mode = FilePermissions.S_IFDIR | 
						NativeConvert.FromOctalPermissionString ("0755");
					stbuf.st_nlink = 2;
					return 0;
				case hello_path:
				case data_path:
				case data_im_path:				
					stbuf.st_mode = FilePermissions.S_IFREG | NativeConvert.FromOctalPermissionString ("0444");
					stbuf.st_nlink = 1;
					int size = 0;
					switch (path) {
						case hello_path:   size = hello_str.Length; break;
						case data_path:
						case data_im_path: size = data_size; break;
					}
					stbuf.st_size = size;
					return 0;
				default:
					stbuf.st_mode = FilePermissions.S_IFREG | NativeConvert.FromOctalPermissionString ("0444");
					stbuf.st_nlink = 1;
					stbuf.st_size = 0;					
					return 0;
			}
		}

		protected override Errno OnReadDirectory (string path, OpenedPathInfo fi, out IEnumerable<DirectoryEntry> paths)
		{
			Console.WriteLine ("(OnReadDirectory {0})", path);
			Trace.WriteLine ("(OnReadDirectory {0})", path);
			paths = null;
			if (path != "/")
				return Errno.ENOENT;

			paths = GetEntries ();			
			return 0;
		}

		private IEnumerable<DirectoryEntry> GetEntries ()
		{
			yield return new DirectoryEntry (".");
			yield return new DirectoryEntry ("..");
			yield return new DirectoryEntry ("hello");
			yield return new DirectoryEntry ("data");
			if (have_data_im)
				yield return new DirectoryEntry ("data.im");
				
			foreach(string key in rootDir.Keys)			
				yield return new DirectoryEntry (key);
			

		}

		protected override Errno OnOpenHandle (string path, OpenedPathInfo fi)
		{
			Console.WriteLine (string.Format ("(OnOpen {0} Flags={1})", path, fi.OpenFlags));
			if (path != hello_path && path != data_path && path != data_im_path)
				return Errno.ENOENT;
			if (path == data_im_path && !have_data_im)
				return Errno.ENOENT;
			if (fi.OpenAccess != OpenFlags.O_RDONLY)
				return Errno.EACCES;
			return 0;
		}

		protected override Errno OnReadHandle (string path, OpenedPathInfo fi, byte[] buf, long offset, out int bytesWritten)
		{
			Console.WriteLine ("(OnRead {0})", path);
			bytesWritten = 0;
			int size = buf.Length;
			if (path == data_im_path)
				FillData ();
			if (path == hello_path || path == data_im_path) {
				byte[] source = path == hello_path ? hello_str : data_im_str;
				if (offset < (long) source.Length) {
					if (offset + (long) size > (long) source.Length)
						size = (int) ((long) source.Length - offset);
					Buffer.BlockCopy (source, (int) offset, buf, 0, size);
				}
				else
					size = 0;
			}
			else if (path == data_path) {
				int max = System.Math.Min ((int) data_size, (int) (offset + buf.Length));
				for (int i = 0, j = (int) offset; j < max; ++i, ++j) {
					if ((j % 27) == 0)
						buf [i] = (byte) '\n';
					else
						buf [i] = (byte) ((j % 26) + 'a');
				}
			}
			else
				return Errno.ENOENT;

			bytesWritten = size;
			return 0;
		}

		protected override Errno OnGetPathExtendedAttribute (string path, string name, byte[] value, out int bytesWritten)
		{
			Trace.WriteLine ("(OnGetPathExtendedAttribute {0})", path);
			bytesWritten = 0;
			if (path != hello_path) {
				return 0;
			}
			byte[] _value;
			lock (hello_attrs) {
				if (!hello_attrs.ContainsKey (name))
					return 0;
				_value = hello_attrs [name];
			}
			if (value.Length < _value.Length) {
				return Errno.ERANGE;
			}
			Array.Copy (_value, value, _value.Length);
			bytesWritten = _value.Length;
			return 0;
		}

		protected override Errno OnSetPathExtendedAttribute (string path, string name, byte[] value, XattrFlags flags)
		{
			Trace.WriteLine ("(OnSetPathExtendedAttribute {0})", path);
			if (path != hello_path) {
				return Errno.ENOSPC;
			}
			lock (hello_attrs) {
				hello_attrs [name] = value;
			}
			return 0;
		}

		protected override Errno OnRemovePathExtendedAttribute (string path, string name)
		{
			Trace.WriteLine ("(OnRemovePathExtendedAttribute {0})", path);
			if (path != hello_path)
				return Errno.ENODATA;
			lock (hello_attrs) {
				if (!hello_attrs.ContainsKey (name))
					return Errno.ENODATA;
				hello_attrs.Remove (name);
			}
			return 0;
		}

		protected override Errno OnListPathExtendedAttributes (string path, out string[] names)
		{
			Trace.WriteLine ("(OnListPathExtendedAttributes {0})", path);
			if (path != hello_path) {
				names = new string[]{};
				return 0;
			}
			List<string> _names = new List<string> ();
			lock (hello_attrs) {
				_names.AddRange (hello_attrs.Keys);
			}
			names = _names.ToArray ();
			return 0;
		}

		private bool ParseArguments (string[] args)
		{
			for (int i = 0; i < args.Length; ++i) {
				switch (args [i]) {
					case "--data.im-in-memory":
						have_data_im = true;
						break;
					case "-h":
					case "--help":
						Console.Error.WriteLine ("usage: sfs [options] device mountpoint");
						FileSystem.ShowFuseHelp ("sfs");
						return false;
					default:
						this.DeviceFile = args [i];
						base.MountPoint = args [i+1];
						i = args.Length;
						break;
				}
			}
			return true;
		}

		private void FillData ()
		{
			lock (data_im_str_lock) {
				if (data_im_str != null)
					return;
				data_im_str = new byte [data_size];
				for (int i = 0; i < data_im_str.Length; ++i) {
					if ((i % 27) == 0)
						data_im_str [i] = (byte) '\n';
					else
						data_im_str [i] = (byte) ((i % 26) + 'a');
				}
			}
		}
		
		public bool InitFs(string fullPath)
		{
			fsDevice = new FuseDevice(fullPath);
				
			Console.WriteLine("Device: {0}", fullPath);
			if (!fsDevice.DeviceOpen()) {				
				return false;
			}
				
			fsDevice.DeviceReadSector(mapDevice, 1);
			fsDevice.DeviceReadSector(dirDevice, 2);
			
			SerializeDirectoryDevice();
			return true;
		}

		public static void Main (string[] args)
		{
			using (FuseSmallFS fs = new FuseSmallFS ()) {
				string fullPath = "";
				string[] unhandled = fs.ParseFuseArguments (args);
				
				foreach (string key in fs.FuseOptions.Keys) {
					Console.WriteLine ("Option: {0}={1}", key, fs.FuseOptions [key]);
				}
				
				Console.WriteLine("Arguments Count: {0}", unhandled.Length);
				
				if (!fs.ParseArguments (unhandled))
					return;
				
				fullPath = Path.GetFullPath(fs.DeviceFile);
				
				if (!fs.InitFs(fullPath))
				{
					Console.WriteLine("Cannot open device file {0}", fullPath);
					return;
				}
																	
				fs.Start ();
			}
		}
	}
}

