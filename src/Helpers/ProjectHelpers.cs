﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace MadsKristensen.AddAnyFile
{
    public static class ProjectHelpers
    {
        static DTE2 _dte = AddAnyFilePackage._dte;

        public static string GetRootNamespace(this Project project)
        {
            if (project == null)
                return null;

            string ns = project.Name ?? string.Empty;

            try
            {
                var prop = project.Properties.Item("RootNamespace");

                if (prop != null && prop.Value != null && !string.IsNullOrEmpty(prop.Value.ToString()))
                    ns = prop.Value.ToString();
            }
            catch { /* Project doesn't have a root namespace */ }

            return CleanNameSpace(ns, stripPeriods: false);
        }

        public static string CleanNameSpace(string ns, bool stripPeriods = true)
        {
            if (stripPeriods)
            {
                ns = ns.Replace(".", "");
            }

            ns = ns.Replace(" ", "")
                     .Replace("-", "")
                     .Replace("\\", ".");

            return ns;
        }

        public static string GetRootFolder(this Project project)
        {
            if (project == null || string.IsNullOrEmpty(project.FullName))
                return null;

            string fullPath;

            try
            {
                fullPath = project.Properties.Item("FullPath").Value as string;
            }
            catch (ArgumentException)
            {
                try
                {
                    // MFC projects don't have FullPath, and there seems to be no way to query existence
                    fullPath = project.Properties.Item("ProjectDirectory").Value as string;
                }
                catch (ArgumentException)
                {
                    // Installer projects have a ProjectPath.
                    fullPath = project.Properties.Item("ProjectPath").Value as string;
                }
            }

            if (string.IsNullOrEmpty(fullPath))
                return File.Exists(project.FullName) ? Path.GetDirectoryName(project.FullName) : null;

            if (Directory.Exists(fullPath))
                return fullPath;

            if (File.Exists(fullPath))
                return Path.GetDirectoryName(fullPath);

            return null;
        }

        public static ProjectItem AddFileToProject(this Project project, string file, string itemType = null)
        {
            if (project.IsKind(ProjectTypes.ASPNET_5))
                return _dte.Solution.FindProjectItem(file);

            var fileContainerItem = project.ProjectItems;

            string relative = PackageUtilities.MakeRelative(project.GetRootFolder(), file);

            foreach (var folder in Path.GetDirectoryName(relative).Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                try 
                {
                    fileContainerItem = fileContainerItem.AddFolder(folder).ProjectItems;
                }

                catch (Exception e) 
                {
                    fileContainerItem = FindFolderNode(fileContainerItem, folder, e);
                }
            }

            var item = fileContainerItem.AddFromFile(file);
            item.SetItemType(itemType);
            return item;
        }

        public static ProjectItems FindFolderNode(ProjectItems projectItems, string folder, Exception e = null)
        {
            foreach (ProjectItem node in projectItems)
            {
                if (node.Kind.Equals(Constants.vsProjectItemKindPhysicalFolder, StringComparison.InvariantCultureIgnoreCase) 
                    && node.Name == folder)
                {
                    return node.ProjectItems;
                }
            }

            throw new ArgumentException($"Failed to find folder { folder } in project.", e);
        }

        public static void SetItemType(this ProjectItem item, string itemType)
        {
            try
            {
                if (item == null || item.ContainingProject == null)
                    return;

                if (string.IsNullOrEmpty(itemType)
                    || item.ContainingProject.IsKind(ProjectTypes.WEBSITE_PROJECT)
                    || item.ContainingProject.IsKind(ProjectTypes.UNIVERSAL_APP))
                    return;

                item.Properties.Item("ItemType").Value = itemType;
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        public static bool IsKind(this Project project, string kindGuid)
        {
            return project.Kind.Equals(kindGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<Project> GetChildProjects(Project parent)
        {
            try
            {
                if (!parent.IsKind(ProjectKinds.vsProjectKindSolutionFolder) && parent.Collection == null)  // Unloaded
                    return Enumerable.Empty<Project>();

                if (!string.IsNullOrEmpty(parent.FullName))
                    return new[] { parent };
            }
            catch (COMException)
            {
                return Enumerable.Empty<Project>();
            }

            return parent.ProjectItems
                    .Cast<ProjectItem>()
                    .Where(p => p.SubProject != null)
                    .SelectMany(p => GetChildProjects(p.SubProject));
        }

        public static Project GetActiveProject()
        {
            try
            {
                var activeSolutionProjects = _dte.ActiveSolutionProjects as Array;

                if (activeSolutionProjects != null && activeSolutionProjects.Length > 0)
                    return activeSolutionProjects.GetValue(0) as Project;

                var doc = _dte.ActiveDocument;

                if (doc != null && !string.IsNullOrEmpty(doc.FullName))
                {
                    var item = (_dte.Solution != null) ? _dte.Solution.FindProjectItem(doc.FullName) : null;

                    if (item != null)
                        return item.ContainingProject;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Error getting the active project" + ex);
            }

            return null;
        }

        public static IWpfTextView GetCurentTextView()
        {
            var componentModel = GetComponentModel();
            if (componentModel == null) return null;
            var editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();

            return editorAdapter.GetWpfTextView(GetCurrentNativeTextView());
        }

        public static IVsTextView GetCurrentNativeTextView()
        {
            var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));

            IVsTextView activeView = null;
            ErrorHandler.ThrowOnFailure(textManager.GetActiveView(1, null, out activeView));
            return activeView;
        }

        public static IComponentModel GetComponentModel()
        {
            return (IComponentModel)AddAnyFilePackage.GetGlobalService(typeof(SComponentModel));
        }
    }

    public static class ProjectTypes
    {
        public const string ASPNET_5 = "{8BB2217D-0F2D-49D1-97BC-3654ED321F3B}";
        public const string WEBSITE_PROJECT = "{E24C65DC-7377-472B-9ABA-BC803B73C61A}";
        public const string UNIVERSAL_APP = "{262852C6-CD72-467D-83FE-5EEB1973A190}";
        public const string NODE_JS = "{9092AA53-FB77-4645-B42D-1CCCA6BD08BD}";
    }
}
