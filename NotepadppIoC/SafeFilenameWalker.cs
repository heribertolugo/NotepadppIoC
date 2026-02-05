using Shell32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NotepadppIoC
{
    internal class SafeFilenameWalker
    {
        private IShellDispatch _shell;
        private int NameSpaceAttempts = 0;
        

        public static IEnumerable<string> EnumerateFiles(string path, bool searchSubdirectories = true)
        {
            var walker = new SafeFilenameWalker();

            return walker.Search(path, searchSubdirectories);

            //DirectoryInfo dInfo = new DirectoryInfo(path);

            //foreach (var file in dInfo.GetFiles())
            //{
            //    yield return file.FullName;
            //}

            //if (searchSubdirectories)
            //{
            //    var dInfos = dInfo.GetDirectories();
            //    foreach (var info in dInfos)
            //    {
            //        foreach (var file in EnumerateFiles(info.FullName, searchSubdirectories))
            //        {
            //            yield return file;
            //        }
            //    }
            //}
        }

        // ************************************************************************** \\
        // This code was taken directly from FileList project with minor modifications \\
        // **************************************************************************** \\
        [STAThread]
        private IEnumerable<string> Search(string path, bool searchSubdirectories = true)
        {
            IShellDispatch shell = _shell;
            Folder objFolder = null;

            try
            {
                objFolder = shell.NameSpace(path);
            }
            catch (Exception)
            {
                // file would be inaccessible to shell class, usually because of access permissions
                // but sometimes, ShellClass seems to get out of sync.
                // in which case it will throw "The object invoked has disconnected from its clients" exception
                // in this scenario, we want to attempt 1 more time to collect extended properties, with a new ShellClass

                //throw;
            }

            if (objFolder == null)
            {
                if (this.NameSpaceAttempts < 1)
                {
                    this.NameSpaceAttempts++;
                    Type shellAppType = Type.GetTypeFromProgID("Shell.Application");
                    this._shell = (IShellDispatch)Activator.CreateInstance(shellAppType);// necessary so we can embed interop;
                    foreach (string f in this.Search(path, searchSubdirectories))
                        yield return f;

                    yield break;
                }

                IEnumerable<string> files = System.Linq.Enumerable.Empty<string>();
                try
                {
                    // some files/folders inaccessible to shell class are picked up by .NET IO
                    // these files are likely link redirects or something. they don't seem to show when you browse explorer  
                    files = this.RegularSearch(path, searchSubdirectories);
                }
                catch (Exception) { }

                foreach (string file in files)
                    yield return file;

                yield break;
            }

            // temp fix for processing hidden files
            // shell objFolder.Items() will not enumerate these items
            bool directoryExists = System.IO.Directory.Exists(path);
            List<string> netItems = directoryExists ? System.IO.Directory.GetFiles(path).ToList() : new List<string>();
            List<string> netFolders = directoryExists ? System.IO.Directory.GetDirectories(path).ToList() : new List<string>();

            foreach (FolderItem2 folderItem2 in objFolder.Items())
            {
                FolderItem2 item = folderItem2;
                string fileData = null;

                if (!item.IsFolder || item.Type.Equals("Compressed (zipped) Folder"))
                {
                    try
                    {
                        fileData = item.Path;
                        netItems.Remove(item.Path);
                    }
                    catch (Exception) { }
                    if (fileData == null)
                        continue;

                    yield return fileData;
                }
                else if (searchSubdirectories && directoryExists)
                {
                    foreach (string file in this.Search(item.Path))
                    {
                        netFolders.Remove(item.Path);
                        yield return file;
                    }
                }
                
                item = null;
            }
            try
            {
                Marshal.ReleaseComObject(objFolder);
            }
            catch (Exception) { }

            foreach (string netItem in netItems)
            {
                string fileData = netItem;
                yield return fileData;
            }
        }

        private IEnumerable<string> RegularSearch(string path, bool searchSubdirectories = true)
        {
            IEnumerable<string> files = AccessableFiles(path);

            foreach (string file in files)
            {
                yield return file;
            }

            if (searchSubdirectories)
                foreach (string directory in AccessableDirectories(path))
                {
                    IEnumerator<string> filez = this.Search(directory, searchSubdirectories).GetEnumerator();
                    while (filez.MoveNext())
                        yield return filez.Current;
                }
        }
        private static IEnumerable<string> AccessableDirectories(string path)
        {
            //List<string> accessable = new List<string>();
            string[] directories = new string[0];

            try
            {
                directories = System.IO.Directory.GetDirectories(path);
            }
            catch (Exception) { }

            foreach (string directory in directories)
            {
                if (IsSystemObjectAccessable(directory))
                    yield return directory;
                //accessable.Add(directory);
            }

            //return accessable;
        }
        private static IEnumerable<string> AccessableFiles(string path)
        {
            //List<string> accessable = new List<string>();
            string[] files = new string[0];

            try
            {
                files = System.IO.Directory.GetFiles(path);
            }
            catch (Exception) { }

            foreach (string file in files)
            {
                if (IsSystemObjectAccessable(file))
                    yield return file;
                //accessable.Add(file);
            }

            //return accessable;
        }

        private static bool IsSystemObjectAccessable(string path)
        {
            try
            {
                if (System.IO.Directory.Exists(path))
                {
                    //System.IO.DirectoryInfo dirInfo = new System.IO.DirectoryInfo(path);
                    //System.Security.AccessControl.DirectorySecurity dirAC = dirInfo.GetAccessControl(System.Security.AccessControl.AccessControlSections.All);
                    System.IO.Directory.GetDirectories(path);
                    System.IO.Directory.GetFiles(path);
                }
                else if (System.IO.File.Exists(path))
                {
                    //System.IO.FileInfo fileInfo = new System.IO.FileInfo(path);
                    //System.Security.AccessControl.FileSecurity fileAC = fileInfo.GetAccessControl(System.Security.AccessControl.AccessControlSections.All);

                    System.IO.FileStream stream = System.IO.File.Open(path, System.IO.FileMode.Open,
                                                    System.IO.FileAccess.Read, System.IO.FileShare.None);
                    stream.Close();
                    //using (System.IO.FileStream reader = new System.IO.FileStream(path, System.IO.FileMode.Open))
                    //{
                    //    byte[] bytes = new byte[1];
                    //    reader.Read(bytes, 0, 1);
                    //}
                }
                else
                {
                    return false;
                    //throw new Exception();
                }
                return true;
            }
            catch (Exception ex)
            {
            }

            return false;
        }
    }
}
