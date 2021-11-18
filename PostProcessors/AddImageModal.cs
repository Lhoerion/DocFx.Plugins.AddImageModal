using System;
using System.Collections.Immutable;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using HtmlAgilityPack;
using Microsoft.DocAsCode.Build.Engine;
using Microsoft.DocAsCode.Common;
using Microsoft.DocAsCode.Plugins;

namespace DocFx.Plugins.AddImageModal
{
    [Export(nameof(AddImageModal), typeof(IPostProcessor))]
    // ReSharper disable once UnusedType.Global
    public class AddImageModal: IPostProcessor
    {
        private bool _disableImageModal;

        public ImmutableDictionary<string, object> PrepareMetadata(ImmutableDictionary<string, object> metadata)
        {
            if (metadata.TryGetValue("_disableImageModal", out var disableImageModal))
            {
                _disableImageModal = (bool)disableImageModal;
            }

            return metadata;
        }

        public Manifest Process(Manifest manifest, string outputFolder)
        {
            if (_disableImageModal)
            {
                return manifest;
            }

            foreach (var file in manifest.Files.Where(f => f.DocumentType == "Conceptual"))
            {
                foreach (var outputFile in file.OutputFiles)
                {
                    AddModalForImages(manifest, outputFile.Value, outputFolder);
                }
            }

            return manifest;
        }

        private void AddModalForImages(Manifest manifest, OutputFileInfo outputFile, string outputFolder)
        {
            var path = Path.Combine(outputFolder, outputFile.RelativePath);
            var htmlDoc = new HtmlDocument();
            htmlDoc.Load(path, Encoding.UTF8);
            const string imgXPath = "//*[not(contains(@class, 'brand'))]/img[not(contains(@class, 'logomark'))]";
            var imgNodes = htmlDoc.DocumentNode.SelectNodes(imgXPath) ?? Enumerable.Empty<HtmlNode>();

            var foundImg = false;
            var processedImgCount = 0;
            foreach (var imgNode in imgNodes)
            {
                if (imgNode.Attributes.Contains("data-nopreview")
                    || imgNode.ParentNode != null && imgNode.ParentNode.Attributes.Contains("data-nopreview"))
                {
                    continue;
                }

                if (Uri.TryCreate(imgNode.Attributes["src"].Value, UriKind.Absolute, out _))
                {
                    continue;
                }

                var imgRsrcs = manifest.Files.Where(el => el.DocumentType == "Resource")
                    .SelectMany(el => el.OutputFiles.Values).ToArray();

                var imgRelPath = ((RelativePath)imgNode.Attributes["src"].Value).BasedOn((RelativePath)outputFile.RelativePath);

                if (imgRsrcs.All(el => el.RelativePath != imgRelPath))
                {
                    continue;
                }

                var imgPreviewRelPath = imgRelPath.ChangeFileName(imgRelPath.GetFileNameWithoutExtension() + "_thumbnail");

                var imgPreviewFile = imgRsrcs.FirstOrDefault(el =>
                    el.RelativePath.Substring(0, el.RelativePath.LastIndexOf('.')) == imgPreviewRelPath);

                if (imgPreviewFile == null)
                {
                    continue;
                }

                var src = imgPreviewRelPath - (RelativePath)outputFile.RelativePath
                          + imgPreviewFile.RelativePath.Substring(imgPreviewFile.RelativePath.LastIndexOf('.'));

                UpdateImageNode(htmlDoc, imgNode, src);

                foundImg = true;
                processedImgCount++;
            }

            if (!foundImg)
            {
                return;
            }

            Logger.LogInfo($"Converted {processedImgCount} images for {path}.");
            AppendRequiredTemplates(htmlDoc, outputFolder, outputFile);
            htmlDoc.Save(path, Encoding.UTF8);
        }

        private static void AppendRequiredTemplates(HtmlDocument htmlDoc, string outputFolder, OutputFileInfo outputFile)
        {
            var test = Load("templates/docfx-plugins-addimagemodal/partials/imageModal.tmpl");
            var htmlRaw = test.Render(new {});
            var htmlDoc3 = new HtmlDocument();
            htmlDoc3.LoadHtml(htmlRaw);

            var htmlDoc2 = new HtmlDocument();
            var path = Path.Combine(outputFolder, outputFile.RelativePath);
            var rel = RelativePath.Empty.MakeRelativeTo((RelativePath)outputFile.RelativePath);
            htmlDoc2.Load(path, Encoding.UTF8);

            var headNode = htmlDoc.DocumentNode.SelectSingleNode("//head");
            var lastLinkNode = htmlDoc.DocumentNode.SelectNodes("//head/link").Last();
            var bodyNode = htmlDoc.DocumentNode.SelectSingleNode("//body");
            var firstScriptNode = htmlDoc.DocumentNode.SelectNodes("//body/script").First();
            var lastScriptNode = htmlDoc.DocumentNode.SelectNodes("//body/script").Last();

            bodyNode.InsertBefore(htmlDoc3.DocumentNode.FirstChild, firstScriptNode);

            var scriptNewEl = htmlDoc.CreateElement("script");
            scriptNewEl.Attributes.Add(htmlDoc.CreateAttribute("type", "text/javascript"));
            scriptNewEl.Attributes.Add(htmlDoc.CreateAttribute("src", rel + "styles/image-modal.js"));
            bodyNode.InsertAfter(scriptNewEl, lastScriptNode);

            var headNewEl = htmlDoc.CreateElement("link");
            headNewEl.Attributes.Add(htmlDoc.CreateAttribute("rel", "stylesheet"));
            headNewEl.Attributes.Add(htmlDoc.CreateAttribute("href", rel + "styles/image-modal.css"));
            headNode.InsertAfter(headNewEl, lastLinkNode);
        }

        private static void UpdateImageNode(HtmlDocument htmlDoc, HtmlNode imgNode, string src)
        {
            imgNode.Attributes.Add(htmlDoc.CreateAttribute("loading", "lazy"));
            imgNode.Attributes.Add(htmlDoc.CreateAttribute("data-toggle", "modal"));
            imgNode.Attributes.Add(htmlDoc.CreateAttribute("data-src", imgNode.Attributes["src"].Value));
            imgNode.Attributes["src"].Value = src;
        }

        private static ITemplateRenderer Load(string filePath)
        {
            var loader = new RendererLoader(new LocalFileResourceReader(Path.GetDirectoryName(filePath)), 64);
            return loader.Load(Path.GetFileName(filePath));
        }
    }
}
