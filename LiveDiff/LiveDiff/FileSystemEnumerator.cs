﻿/*
LiveDiff: A portable system-level differencing tool
thomas@thomaslaurenson.com
https://github.com/thomaslaurenson/LiveDiff

Copyright(C) 2018 Thomas Laurenson 

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.If not, see<http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace LiveDiff
{
    class FileSystemEnumerator
    {
        public static List<FileInformation> files = new List<FileInformation>();
        public static List<DirectoryInformation> directories = new List<DirectoryInformation>();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public FileAttributes dwFileAttributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        public static bool FindNextFilePInvokeRecursiveParalleled(string path)
        {
            List<FileInformation> fileList = new List<FileInformation>();
            object fileListLock = new object();
            List<DirectoryInformation> directoryList = new List<DirectoryInformation>();
            object directoryListLock = new object();
            WIN32_FIND_DATAW findData;
            IntPtr findHandle = INVALID_HANDLE_VALUE;
            List<Tuple<string, DateTime>> info = new List<Tuple<string, DateTime>>();
            try
            {
                path = path.EndsWith(@"\") ? path : path + @"\";
                findHandle = FindFirstFileW(path + @"*", out findData);
                if (findHandle != INVALID_HANDLE_VALUE)
                {
                    do
                    {
                        // Skip current directory and parent directory symbols that are returned.
                        if (findData.cFileName != "." && findData.cFileName != "..")
                        {
                            string fullPath = path + findData.cFileName;
                            // Check if this is a directory and not a symbolic link since symbolic links could lead to repeated files and folders as well as infinite loops.
                            if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory) && !findData.dwFileAttributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                directoryList.Add(new DirectoryInformation { FullPath = fullPath });
                            }
                            else if (!findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                            {
                                fileList.Add(new FileInformation { FullPath = fullPath });
                            }
                        }
                    }
                    while (FindNextFile(findHandle, out findData));
                    directoryList.AsParallel().ForAll(x =>
                    {
                        List<FileInformation> subDirectoryFileList = new List<FileInformation>();
                        List<DirectoryInformation> subDirectoryDirectoryList = new List<DirectoryInformation>();
                        if (FindNextFilePInvokeRecursive(x.FullPath, out subDirectoryFileList, out subDirectoryDirectoryList))
                        {
                            lock (fileListLock)
                            {
                                fileList.AddRange(subDirectoryFileList);
                            }
                            lock (directoryListLock)
                            {
                                directoryList.AddRange(subDirectoryDirectoryList);
                            }
                        }
                    });
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Caught exception while trying to enumerate a directory. {0}", exception.ToString());
                if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
                files = null;
                directories = null;
                return false;
            }
            if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
            files = fileList;
            directories = directoryList;
            return true;
        }

        static bool FindNextFilePInvokeRecursive(string path, out List<FileInformation> files, out List<DirectoryInformation> directories)
        {
            List<FileInformation> fileList = new List<FileInformation>();
            List<DirectoryInformation> directoryList = new List<DirectoryInformation>();
            WIN32_FIND_DATAW findData;
            IntPtr findHandle = INVALID_HANDLE_VALUE;
            List<Tuple<string, DateTime>> info = new List<Tuple<string, DateTime>>();
            try
            {
                findHandle = FindFirstFileW(path + @"\*", out findData);
                if (findHandle != INVALID_HANDLE_VALUE)
                {
                    do
                    {
                        // Skip current directory and parent directory symbols that are returned.
                        if (findData.cFileName != "." && findData.cFileName != "..")
                        {
                            string fullPath = path + @"\" + findData.cFileName;
                            // Check if this is a directory and not a symbolic link since symbolic links could lead to repeated files and folders as well as infinite loops.
                            if (findData.dwFileAttributes.HasFlag(FileAttributes.Directory) && !findData.dwFileAttributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                directoryList.Add(new DirectoryInformation { FullPath = fullPath });
                                List<FileInformation> subDirectoryFileList = new List<FileInformation>();
                                List<DirectoryInformation> subDirectoryDirectoryList = new List<DirectoryInformation>();
                                if (FindNextFilePInvokeRecursive(fullPath, out subDirectoryFileList, out subDirectoryDirectoryList))
                                {
                                    fileList.AddRange(subDirectoryFileList);
                                    directoryList.AddRange(subDirectoryDirectoryList);
                                }
                            }
                            else if (!findData.dwFileAttributes.HasFlag(FileAttributes.Directory))
                            {
                                fileList.Add(new FileInformation { FullPath = fullPath });
                            }
                        }
                    }
                    while (FindNextFile(findHandle, out findData));
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Caught exception while trying to enumerate a directory. {0}", exception.ToString());
                if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
                files = null;
                directories = null;
                return false;
            }
            if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
            files = fileList;
            directories = directoryList;
            return true;
        }

        public class FileInformation
        {
            public string FullPath;
            public DateTime LastWriteTime;
        }

        public class DirectoryInformation
        {
            public string FullPath;
            public DateTime LastWriteTime;
        }
    }
}
