﻿using LibBundle;
using LibGGPK2.Records;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibGGPK2
{
    /// <summary>
    /// Container for handling GGPK file
    /// </summary>
    public class GGPKContainer : IDisposable
    {
        public static readonly BundleSortComp BundleComparer = new BundleSortComp();

        public readonly FileStream fileStream;
        public readonly BinaryReader Reader;
        public readonly BinaryWriter Writer;
        public readonly GGPKRecord ggpkRecord;
        public readonly DirectoryRecord rootDirectory;
        public readonly DirectoryRecord OriginalBundles2;
        public readonly BundleDirectoryNode FakeBundles2;
        public readonly IndexContainer Index;
        public readonly FileRecord IndexRecord;
        public readonly LinkedList<FreeRecord> LinkedFreeRecords = new LinkedList<FreeRecord>();
        public readonly Dictionary<LibBundle.Records.BundleRecord, FileRecord> RecordOfBundle = new Dictionary<LibBundle.Records.BundleRecord, FileRecord>();

        /// <summary>
        /// Load GGPK
        /// </summary>
        /// <param name="path">Path to GGPK file</param>
        public GGPKContainer(string path)
        {
            // Open File
            fileStream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Reader = new BinaryReader(fileStream);
            Writer = new BinaryWriter(fileStream);

            // Read ROOT Directory Record
            BaseRecord ggpk;
            while (!((ggpk = GetRecord()) is GGPKRecord));
            ggpkRecord = ggpk as GGPKRecord;
            rootDirectory = GetRecord(ggpkRecord.RootDirectoryOffset) as DirectoryRecord;
            rootDirectory.Name = "ROOT";

            // Build Linked FreeRecord List
            long NextFreeOffset = ggpkRecord.FirstFreeRecordOffset;
            while (NextFreeOffset > 0)
            {
                FreeRecord current = GetRecord(NextFreeOffset) as FreeRecord;
                LinkedFreeRecords.AddLast(current);
                NextFreeOffset = current.NextFreeOffset;
            }

            // Read Bundles
            OriginalBundles2 = rootDirectory.Children.First(d => d.GetNameHash() == MurmurHash2Unsafe.Hash("bundles2", 0)) as DirectoryRecord;
            if (OriginalBundles2.Children.FirstOrDefault(r => r.Name == "_.index.bin") is FileRecord _index)
            {
                IndexRecord = _index;
                fileStream.Seek(_index.DataBegin, SeekOrigin.Begin);
                Index = new IndexContainer(Reader);
                FakeBundles2 = new BundleDirectoryNode("Bundles2", MurmurHash2Unsafe.Hash("bundles2", 0), (int)OriginalBundles2.Offset, OriginalBundles2.Length, this);
                rootDirectory.Children.Remove(OriginalBundles2);
                rootDirectory.Children.Add(FakeBundles2);
                foreach (var f in Index.Files)
                    BuildBundleTree(f);
            }
            foreach (var br in Index.Bundles)
                RecordOfBundle[br] = (FileRecord)FindRecord(br.Name, OriginalBundles2);
        }

        public void BuildBundleTree(LibBundle.Records.FileRecord fr)
        {
            var SplittedPath = fr.path.Split('/');
            RecordTreeNode parent = FakeBundles2;
            for (int i = 0; i < SplittedPath.Length; i++)
            {
                var name = SplittedPath[i];
                var isFile = (i + 1 == SplittedPath.Length);
                var parentOfFile = (i + 2 == SplittedPath.Length);
                var next = parent.GetChildItem(name);
                if (next == null)
                { // No exist node, Build a new node
                    if (isFile)
                        next = new BundleFileNode(name, fr.Hash, fr.Offset, fr.Size, fr, this);
                    else
                        next = new BundleDirectoryNode(name, MurmurHash2Unsafe.Hash(name.ToLower(), 0), parentOfFile ? fr.parent.Offset : 0, parentOfFile ? fr.parent.Size : 0, this);
                    parent.Children.Add(next);
                    next.Parent = parent;
                }
                parent = next;
            }
        }

        /// <summary>
        /// Read a record from GGPK at <paramref name="offset"/>
        /// </summary>
        public BaseRecord GetRecord(long? offset = null)
        {
            if (offset.HasValue)
                fileStream.Seek(offset.Value, SeekOrigin.Begin);
            var length = Reader.ReadInt32();
            var tag = Reader.ReadBytes(4);
            if (tag.SequenceEqual(FileRecord.Tag))
                return new FileRecord(length, this);
            else if (tag.SequenceEqual(FreeRecord.Tag))
                return new FreeRecord(length, this);
            else if (tag.SequenceEqual(DirectoryRecord.Tag))
                return new DirectoryRecord(length, this);
            else if (tag.SequenceEqual(GGPKRecord.Tag))
                return new GGPKRecord(length, this);
            else
                throw new Exception("Invalid Record Tag: " + Encoding.ASCII.GetString(tag) + " at offset: " + (fileStream.Position - 8).ToString());
        }

        /// <summary>
        /// Find the record with a <paramref name="path"/>
        /// </summary>
        public RecordTreeNode FindRecord(string path, RecordTreeNode parent = null)
        {
            var SplittedPath = path.Split(new char[] { '/', '\\' });
            if (parent == null)
                parent = rootDirectory;
            for (int i = 0; i < SplittedPath.Length; i++)
            {
                var name = SplittedPath[i];
                var next = parent.GetChildItem(name);
                if (next == null)
                    return null;
                parent = next;
            }
            return parent;
        }

        /// <summary>
        /// Defragment the GGPK asynchronously.
        /// Currently isn't implemented.
        /// Throw a <see cref="NotImplementedException"/>.
        /// </summary>
        public async Task DefragmentAsync()
        {
            await Task.Delay(1).ConfigureAwait(false);
            Defragment();
        }

        /// <summary>
        /// Defragment the GGPK synchronously.
        /// Currently isn't implemented.
        /// Throw a <see cref="NotImplementedException"/>.
        /// </summary>
        public void Defragment()
        {
            throw new NotImplementedException();
            //TODO
        }

        public void Dispose()
        {
            Writer.Flush();
            Writer.Close();
            Reader.Close();
        }

        /// <summary>
        /// Export files asynchronously
        /// </summary>
        /// <param name="record">File/Directory Record to export</param>
        /// <param name="path">Path to save</param>
        /// <param name="ProgressStep">It will be executed every time a file is exported</param>
        public static async Task<Exception> ExportAsync(ICollection<KeyValuePair<IFileRecord, string>> list, Action ProgressStep = null)
        {
            await Task.Delay(1).ConfigureAwait(false);
            try
            {
                Export(list, ProgressStep);
            }
            catch (Exception ex)
            {
                return ex;
            }
            return null;
        }

        /// <summary>
        /// Export files synchronously
        /// </summary>
        /// <param name="list">File list to export (generate by <see cref="RecursiveFileList"/>)</param>
        /// <param name="ProgressStep">It will be executed every time a file is exported</param>
        public static void Export(ICollection<KeyValuePair<IFileRecord, string>> list, Action ProgressStep = null)
        {
            LibBundle.Records.BundleRecord br = null;
            MemoryStream ms = null;
            foreach (var record in list)
            {
                Directory.CreateDirectory(Directory.GetParent(record.Value).FullName);
                if (record.Key is BundleFileNode bfn)
                {
                    if (br != bfn.BundleFileRecord.bundleRecord)
                    {
                        ms?.Close();
                        br = bfn.BundleFileRecord.bundleRecord;
                        br.Read(bfn.ggpkContainer.Reader, bfn.ggpkContainer.RecordOfBundle[bfn.BundleFileRecord.bundleRecord].DataBegin);
                        ms = br.Bundle.Read(bfn.ggpkContainer.Reader);
                    }
                    File.WriteAllBytes(record.Value, bfn.BatchReadFileContent(ms));
                }
                else
                    File.WriteAllBytes(record.Value, record.Key.ReadFileContent());
                ProgressStep();
            }
        }

        /// <summary>
        /// Replace files asynchronously
        /// </summary>
        /// <param name="list">File list to replace (generate by <see cref="RecursiveFileList"/>)</param>
        /// <param name="ProgressStep">It will be executed every time a file is replaced</param>
        public async Task<Exception> ReplaceAsync(ICollection<KeyValuePair<IFileRecord, string>> list, Action ProgressStep = null)
        {
            await Task.Delay(1).ConfigureAwait(false);
            try
            {
                Replace(list, ProgressStep);
            }
            catch (Exception ex)
            {
                return ex;
            }
            return null;
        }

        /// <summary>
        /// Replace files synchronously
        /// </summary>
        /// <param name="list">File list to replace (generate by <see cref="RecursiveFileList"/>)</param>
        /// <param name="ProgressStep">It will be executed every time a file is replaced</param>
        public void Replace(ICollection<KeyValuePair<IFileRecord, string>> list, Action ProgressStep = null)
        {
            LibBundle.Records.BundleRecord br = null;
            foreach (var record in list)
            {
                if (record.Key is BundleFileNode bfn)
                {
                    bfn.BatchReplaceContent(File.ReadAllBytes(record.Value), out var bundle);
                    if (br != bundle)
                    {
                        if (br != null)
                            RecordOfBundle[br].ReplaceContent(br.Save(Reader));
                        br = bundle;
                        br.Read(Reader, RecordOfBundle[br].DataBegin);
                    }
                }
                else
                    record.Key.ReplaceContent(File.ReadAllBytes(record.Value));
                ProgressStep();
            }
            IndexRecord.ReplaceContent(Index.Save());
        }

        /// <summary>
        /// Get the file list to export
        /// </summary>
        /// <param name="record">File/Directory Record to export</param>
        /// <param name="path">Path to save</param>
        /// <param name="paths">File list</param>
        /// <param name="export">True for export False for replace</param>
        public static void RecursiveFileList(RecordTreeNode record, string path, ref SortedDictionary<IFileRecord, string> paths, bool export)
        {
            if (record is IFileRecord fr)
            {
                if (export || File.Exists(path))
                    paths.Add(fr, path);
            }
            else
                foreach (var f in record.Children)
                    RecursiveFileList(f, path + "\\" + f.Name, ref paths, export);
        }
    }

    /// <summary>
    /// Use to sort the files by their bundle.
    /// </summary>
    public class BundleSortComp : IComparer<IFileRecord>
    {
        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string x, string y);
        public virtual int Compare(IFileRecord x, IFileRecord y)
        {
            if (x is FileRecord frx)
                if (y is FileRecord fry)
                    return frx.DataBegin > fry.DataBegin ? 1 : -1;
                else
                    return -1;
            else
                if (y is FileRecord)
                return 1;
            else
            {
                var bfx = (BundleFileNode)x;
                var bfy = (BundleFileNode)y;
                var ofx = bfx.ggpkContainer.RecordOfBundle[bfx.BundleFileRecord.bundleRecord].DataBegin;
                var ofy = bfy.ggpkContainer.RecordOfBundle[bfy.BundleFileRecord.bundleRecord].DataBegin;
                if (ofx > ofy)
                    return 1;
                else if (ofx < ofy)
                    return -1;
                else
                    return bfx.Offset > bfy.Offset ? 1 : -1;
            }
        }
    }
}