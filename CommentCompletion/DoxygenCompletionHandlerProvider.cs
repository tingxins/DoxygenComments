using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;

namespace DoxygenComments
{
    [Export(typeof(IVsTextViewCreationListener))]
    [Name("C++ Doxygen Completion Handler")]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    class DoxygenCompletionHandlerProvider : IVsTextViewCreationListener
    {
        [Import]
        public IVsEditorAdaptersFactoryService AdapterService = null;

        [Import]
        public ICompletionBroker CompletionBroker { get; set; }

        [Import]
        public SVsServiceProvider ServiceProvider { get; set; }

        [Import]
        public ITextDocumentFactoryService textDocumentFactory { get; set; }

        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            try
            {
                //textDocumentFactory.TextDocumentCreated += new EventHandler<TextDocumentEventArgs>(tdfs_TextDocumentCreated);
                DoxygenFileSystemWatcher.Instance(textDocumentFactory);
                IWpfTextView textView = this.AdapterService.GetWpfTextView(textViewAdapter);
                if (textView == null)
                {
                    return;
                }

                Func<DoxygenCompletionHandler> createCommandHandler = delegate ()
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    var dte = (DTE2)ServiceProvider.GetService(typeof(DTE));
                    var vsShell = (IVsShell)ServiceProvider.GetService(typeof(IVsShell));

                    return new DoxygenCompletionHandler(textViewAdapter, textView, this, textDocumentFactory, dte, vsShell);
                };

                textView.Properties.GetOrCreateSingletonProperty(createCommandHandler);
            }
            catch
            {
            }
        }
        /**void tdfs_TextDocumentCreated(object sender, TextDocumentEventArgs e)
        {
            var a = sender;
            var b = e;
            System.Diagnostics.Debug.WriteLine("tdfs_TextDocumentCreated");
            // I should manipulate the content of the text document here, but
            // this event handler is not called for documents that are initially
            // loaded by the solution or for the first document opened by the user.
        }*/
    }


    public class DoxygenFileSystemWatcher
    {
        public static DoxygenFileSystemWatcher instance;
        //public ITextDocumentFactoryService textDocumentFactory { get; set; }
        //static FileSystemWatcher watcher;

        public IDictionary<string, string> results = new Dictionary<string, string>();
        public SettingsHelper m_settings;

        List<String> fileTypes = new List<String>(){ "*.cpp", "*.c", "*.h", "*.m", "*.swift" };
        List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();

        protected DoxygenFileSystemWatcher()
        {
        }
        public static DoxygenFileSystemWatcher Instance(ITextDocumentFactoryService textDocument)
        {
            // Uses lazy initialization.
            // Note: this is not thread safe.
            if (instance == null)
            {
                instance = new DoxygenFileSystemWatcher();
                instance.Setup(textDocument);
            }
            return instance;
        }

        public void Setup(ITextDocumentFactoryService textDocument)
        {
            textDocument.TextDocumentCreated += new EventHandler<TextDocumentEventArgs>(tdfs_TextDocumentCreated);
            SetupWatcher();
        }
        void tdfs_TextDocumentCreated(object sender, TextDocumentEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("tdfs_TextDocumentCreated");
            // I should manipulate the content of the text document here, but
            // this event handler is not called for documents that are initially
            // loaded by the solution or for the first document opened by the user.
        }
        void SetupWatcher()
        {
            DTE2 dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
            var solutionFullName = dte.Solution.FullName;
            foreach (string f in fileTypes)
            {
                FileSystemWatcher watcher = new FileSystemWatcher(solutionFullName);
                watcher.Created += OnCreated;
                watcher.Filter = f;
                watcher.IncludeSubdirectories = true;
                watcher.EnableRaisingEvents = true;
                watchers.Add(watcher);
            }
            m_settings = new SettingsHelper();

        }

        private static void OnCreated(object sender, FileSystemEventArgs e)
        {
            string value = $"Created: {e.FullPath}";
            var results = e.FullPath.Split('\\');
            if(results.Length > 0)
            {
                var fileName = results[results.Length - 1];
                var fileInfos = fileName.Split('.');
                if (fileInfos.Length < 2) { return; }
                var ext = fileInfos[1];

                if (DoxygenFileSystemWatcher.instance.fileTypes.Contains("*." + ext))
                {
                    DoxygenFileSystemWatcher.instance.results.Add(fileName, e.FullPath);
                    System.Diagnostics.Debug.WriteLine("OnCreated: {0}", fileName);
                    string path = e.FullPath;
                    // Create a file to write to.
                    string old = "";
                    var m_settings = DoxygenFileSystemWatcher.instance.m_settings;
                    using (StreamReader sr = File.OpenText(path))
                    {
                        old = sr.ReadToEnd();
                    }
                    if (old.Length > 3)
                    {
                        var headerShortcut = old.Substring(0, 3);
                        if (headerShortcut == "/**")
                        {
                            return;
                        }
                    }
                    using (StreamWriter sw = File.CreateText(path))
                    {
                        string format = m_settings.GetEncodeEscapeChar(m_settings.HeaderFormat);
                        string headerComment = DoxygenFileSystemWatcher.instance.GetFinalFormat(format, e.FullPath, "", "", "");
                        string text = headerComment + "\n" + old;
                        sw.WriteLine(text);
                    }
                }
                /***
                 * 
                string file = "";
                ITextDocument document;
                if (m_document.TryGetTextDocument(m_textView.TextBuffer, out document))
                {
                    var path = document.FilePath.Split('\\');
                    file = path[path.Length - 1];
                    var res = DoxygenFileSystemWatcher.instance.results;
                    if (res.ContainsKey(file))
                    {
                        res.Remove(file);
                    }
                }
                 */
            }
        }

        private string GetFinalFormat(string format, string filePath, string brief, string spaces, string lineEnding)
        {
            //ThreadHelper.ThrowIfNotOnUIThread();
            /// Use the correct line endings and indent
            //format = Regex.Replace(format, @"(\r\n)|(\r|\n)", lineEnding + spaces);

            // Replace all variables with the correct values
            // Specififc variables like $PARAMS and $RETURN must be handled before in
            // a function specific part
            if (format.Contains("$BRIEF"))
            {
                format = format.Replace("$BRIEF", brief);
            }
            if (format.Contains("$MONTH_2"))
            {
                var month = DateTime.Now.Month.ToString().PadLeft(2, '0');
                format = format.Replace("$MONTH_2", month);
            }
            if (format.Contains("$MONTH"))
            {
                var month = DateTime.Now.Month.ToString();
                format = format.Replace("$MONTH", month);
            }
            if (format.Contains("$DAY_OF_MONTH_2"))
            {
                var day = DateTime.Now.Day.ToString().PadLeft(2, '0');
                format = format.Replace("$DAY_OF_MONTH_2", day);
            }
            if (format.Contains("$DAY_OF_MONTH"))
            {
                var day = DateTime.Now.Day.ToString();
                format = format.Replace("$DAY_OF_MONTH", day);
            }
            if (format.Contains("$YEAR"))
            {
                var year = DateTime.Now.Year.ToString();
                format = format.Replace("$YEAR", year);
            }
            if (format.Contains("$FILENAME"))
            {
                var path = filePath.Split('\\');
                string file = path[path.Length - 1];
                format = format.Replace("$FILENAME", file);
            }
            if (format.Contains("$USERNAME"))
            {
                var username = Environment.UserName;
                format = format.Replace("$USERNAME", username);
            }
            if (format.Contains("$PROJECTNAME"))
            {
                DTE2 dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                var solutionFullName = dte.Solution.FullName;
                if (solutionFullName.Length > 0)
                {
                    var splitNames = solutionFullName.Split('\\');
                    var projectName = splitNames[splitNames.Length - 1];
                    format = format.Replace("$PROJECTNAME", projectName);
                }
            }

            if (format.Contains("$END"))
            {
                format = format.Replace("$END", "");
            }
            format = m_settings.GetDecodedEscapeChar(format);

            return format;
        }
    }

}
