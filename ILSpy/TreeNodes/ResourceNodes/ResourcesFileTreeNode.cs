// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Resources;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.ILSpy.Controls;
using ICSharpCode.ILSpy.TextView;
using Microsoft.Win32;

namespace ICSharpCode.ILSpy.TreeNodes
{
	[Export(typeof(IResourceNodeFactory))]
	sealed class ResourcesFileTreeNodeFactory : IResourceNodeFactory
	{
		public ILSpyTreeNode CreateNode(Resource resource)
		{
			if (resource.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) {
				return new ResourcesFileTreeNode(resource);
			}
			return null;
		}

		public ILSpyTreeNode CreateNode(string key, object data)
		{
			return null;
		}
	}

	sealed class ResourcesFileTreeNode : ResourceTreeNode
	{
		readonly ICollection<KeyValuePair<string, string>> stringTableEntries = new ObservableCollection<KeyValuePair<string, string>>();
		readonly ICollection<SerializedObjectRepresentation> otherEntries = new ObservableCollection<SerializedObjectRepresentation>();

		public ResourcesFileTreeNode(Resource er)
			: base(er)
		{
			this.LazyLoading = true;
		}

		public override object Icon
		{
			get { return Images.ResourceResourcesFile; }
		}

		protected override void LoadChildren()
		{
			Stream s = Resource.TryOpenStream();
			if (s == null) return;
			s.Position = 0;
			ResourceReader reader;
			try {
				reader = new ResourceReader(s);
			}
			catch (ArgumentException) {
				return;
			}
			foreach (DictionaryEntry entry in reader.Cast<DictionaryEntry>().OrderBy(e => e.Key.ToString())) {
				ProcessResourceEntry(entry);
			}
		}

		private void ProcessResourceEntry(DictionaryEntry entry)
		{
			var keyString = entry.Key.ToString();

			if (entry.Value is String) {
				stringTableEntries.Add(new KeyValuePair<string, string>(keyString, (string)entry.Value));
				return;
			}

			if (entry.Value is byte[]) {
				Children.Add(ResourceEntryNode.Create(keyString, new MemoryStream((byte[])entry.Value)));
				return;
			}

			var node = ResourceEntryNode.Create(keyString, entry.Value);
			if (node != null) {
				Children.Add(node);
				return;
			}

			string entryType = entry.Value.GetType().FullName;
			if (entry.Value is System.Globalization.CultureInfo) {
				otherEntries.Add(new SerializedObjectRepresentation(keyString, entryType, ((System.Globalization.CultureInfo)entry.Value).DisplayName));
			} else {
				otherEntries.Add(new SerializedObjectRepresentation(keyString, entryType, entry.Value.ToString()));
			}
		}
		
		public override bool Save(DecompilerTextView textView)
		{
			Stream s = Resource.TryOpenStream();
			if (s == null) return false;
			SaveFileDialog dlg = new SaveFileDialog();
			dlg.FileName = DecompilerTextView.CleanUpName(Resource.Name);
			dlg.Filter = "Resources file (*.resources)|*.resources|Resource XML file|*.resx";
			if (dlg.ShowDialog() == true) {
				s.Position = 0;
				switch (dlg.FilterIndex) {
					case 1:
						using (var fs = dlg.OpenFile()) {
							s.CopyTo(fs);
						}
						break;
					case 2:
						var reader = new ResourceReader(s);
						using (var writer = new ResXResourceWriter(dlg.OpenFile())) {
							foreach (DictionaryEntry entry in reader) {
								writer.AddResource(entry.Key.ToString(), entry.Value);
							}
						}
						break;
				}
			}
			return true;
		}

		public override void Decompile(Language language, ITextOutput output, DecompilationOptions options)
		{
			EnsureLazyChildren();
			base.Decompile(language, output, options);
			if (stringTableEntries.Count != 0) {
				ISmartTextOutput smartOutput = output as ISmartTextOutput;
				if (null != smartOutput) {
					smartOutput.AddUIElement(
						delegate {
							return new ResourceStringTable(stringTableEntries, MainWindow.Instance.mainPane);
						}
					);
				}
				output.WriteLine();
				output.WriteLine();
			}
			if (otherEntries.Count != 0) {
				ISmartTextOutput smartOutput = output as ISmartTextOutput;
				if (null != smartOutput) {
					smartOutput.AddUIElement(
						delegate {
							return new ResourceObjectTable(otherEntries, MainWindow.Instance.mainPane);
						}
					);
				}
				output.WriteLine();
			}
		}

		internal class SerializedObjectRepresentation
		{
			public SerializedObjectRepresentation(string key, string type, string value)
			{
				this.Key = key;
				this.Type = type;
				this.Value = value;
			}

			public string Key { get; private set; }
			public string Type { get; private set; }
			public string Value { get; private set; }
		}
	}
}
