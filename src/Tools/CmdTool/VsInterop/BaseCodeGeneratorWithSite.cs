#region Copyright 2009-2010 by Roger Knapp, Licensed under the Apache License, Version 2.0
/* Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
#endregion
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CSharpTest.Net.CustomTool.Interfaces;
using CSharpTest.Net.Utils;

namespace CSharpTest.Net.CustomTool.VsInterop
{
	public abstract class BaseCodeGeneratorWithSite : BaseCodeGenerator, IObjectWithSite
	{
		private const int E_NOINTERFACE = unchecked((int)0x80004002);

		private object site = null;
		private ServiceProvider serviceProvider = null;

		void IObjectWithSite.SetSite(object pUnkSite)
		{
			site = pUnkSite;
			serviceProvider = null;
		}

		void IObjectWithSite.GetSite(ref Guid riid, object[] ppvSite)
		{

			if (ppvSite == null)
			{
				throw new ArgumentNullException("ppvSite");
			}
			if (ppvSite.Length < 1)
			{
				throw new ArgumentException("ppvSite array must have at least 1 member", "ppvSite");
			}

			if (site == null)
			{
				throw new COMException("object is not sited", E_FAIL);
			}

			IntPtr pUnknownPointer = Marshal.GetIUnknownForObject(site);
			IntPtr intPointer = IntPtr.Zero;
			Marshal.QueryInterface(pUnknownPointer, ref riid, out intPointer);

			if (intPointer == IntPtr.Zero)
			{
				throw new COMException("site does not support requested interface", E_NOINTERFACE);
			}

			ppvSite[0] = Marshal.GetObjectForIUnknown(intPointer);
		}

		private ServiceProvider SiteServiceProvider
		{
			get
			{
				if (serviceProvider == null)
				{
					IOleServiceProvider oleServiceProvider = site as IOleServiceProvider;
					Debug.Assert(oleServiceProvider != null, "Unable to get IOleServiceProvider from site object.");

					serviceProvider = new ServiceProvider(oleServiceProvider);
				}
				return serviceProvider;
			}
		}

		protected EnvDTE.ProjectItem ProjectItem
		{
			get
			{
				object serviceObject = SiteServiceProvider.GetService(typeof(EnvDTE.ProjectItem));
				Debug.Assert(serviceObject != null, "Unable to get Project Item.");
				if (serviceObject == null)
				{
					string errorMessage = String.Format("Unable to obtain ProjectItem.");
					Error(errorMessage, 0, 0);
					return null;
				}
				return ((EnvDTE.ProjectItem)serviceObject);
			}
		}

        protected void AddProjectFile(string filename)
        {
            if (!File.Exists(filename))
                return;

            EnvDTE.ProjectItem parent = this.ProjectItem;
            EnvDTE.ProjectItem item = null;

            for (int i = 1; item == null && i <= parent.ProjectItems.Count; i++)
            {
                EnvDTE.ProjectItem child = parent.ProjectItems.Item(i);
                if (StringComparer.OrdinalIgnoreCase.Equals(child.Name, Path.GetFileName(filename)))
                    item = child;
            }

            if (item == null)
                item = parent.ProjectItems.AddFromFile(filename);
        }

		protected string ProjectItemPath
		{
			get
			{
				short count = ProjectItem.FileCount;
				if(count == 0)
					throw new ApplicationException("Unable to locate path for item.");

				return ProjectItem.get_FileNames(0);
			}
		}

        private EnvDTE.OutputWindowPane GetOutputPane()
        {
            string windowName = GetType().Name;
            EnvDTE.OutputWindowPane pane = null;
            EnvDTE.Window window = ProjectItem.DTE.Windows.Item(EnvDTE.Constants.vsWindowKindOutput);
            if (window != null)
            {
                EnvDTE.OutputWindow output = window.Object as EnvDTE.OutputWindow;
                if (output != null)
                {
                    pane = output.ActivePane;
                    if (pane == null || pane.Name != windowName)
                    {
                        for (int ix = output.OutputWindowPanes.Count; ix > 0; ix--)
                        {
                            pane = output.OutputWindowPanes.Item(ix);
                            if (pane.Name == windowName)
                                break;
                        }
                        if (pane == null || pane.Name != windowName)
                            pane = output.OutputWindowPanes.Add(windowName);
                        if (pane != null)
                            pane.Activate();
                    }
                }
            }
            return pane;
        }

		protected override void OnBeforeGenerate(string defaultNamespace, string inputFileName)
		{
			ClearOutput();
			base.OnBeforeGenerate(defaultNamespace, inputFileName);
		}

		protected override void OnError(Exception e)
		{
			base.OnError(e);
			WriteLine(e.ToString());
		}

        protected void ClearOutput()
        {
            EnvDTE.OutputWindowPane pane = GetOutputPane();
            if(pane != null)
                pane.Clear();
        }

        protected void WriteLine(string message)
        {
            if (message == null)
                return;

            Log.Info(message);

            EnvDTE.OutputWindowPane pane = GetOutputPane();
            if (pane == null)
                return;

            using(StringReader sr = new StringReader(message))
            {
                string line;
                while(null != (line = sr.ReadLine()))
                {
                    bool writeRaw = true;
                    try
                    {
						Match match = RegexPatterns.VSErrorMessage.Match(message);
                        if (match.Success && File.Exists(match.Groups["path"].Value))
                        {
                            int lineNo;
                            int.TryParse(match.Groups["line"].Value, out lineNo);
                            pane.OutputTaskItemString(line + Environment.NewLine,
                                                      EnvDTE.vsTaskPriority.vsTaskPriorityMedium,
                                                      pane.Name, EnvDTE.vsTaskIcon.vsTaskIconCompile,
                                                      match.Groups["path"].Value, lineNo,
                                                      match.Groups["message"].Value, true);
                            writeRaw = false;
                        }
                    }
                    catch { writeRaw = true; }
                    if(writeRaw)
                        pane.OutputString(line + Environment.NewLine);
                }
            }
        }
	}
}