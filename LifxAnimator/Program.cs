﻿using CommandLine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace LifxAnimator
{
    class Program
    {
        class Options
        {
            [Option("path", Required = true, HelpText = "Path of sequence image."
                + " Pixel rows correspond to lights and pixel columns correspond to frames.")]
            public string Path { get; set; }

            [Option("lights", Required = true, Min = 1, HelpText = "Space-separated, ordered list of IP addresses."
                + " The first light maps to the topmost pixel row of the sequence image.")]
            public IReadOnlyCollection<string> Lights { get; set; }

            [Option("fps", Default = 20)]
            public int FramesPerSecond { get; set; }

            [Option("repeat-count")]
            public int? RepeatCount { get; set; }

            [Option("repeat-seconds")]
            public int? RepeatSeconds { get; set; }

            [Option("smooth-transitions", HelpText = "Smoothly adjust color and brightness when transitioning frames.")]
            public bool SmoothTransitions { get; set; }

            [Option("brightness-factor", Default = 1f, HelpText = "Scales brightness so you don't need sunglasses while testing.")]
            public float BrightnessFactor { get; set; }

            public IEnumerable<string> Validate()
            {
                if (!File.Exists(Path))
                    yield return "Path does not exist.";

                var image = Image.Load(Path);

                if (image.Height < Lights.Count())
                    yield return $"Sequence can't handle more than {image.Height} light(s).";

                if (RepeatCount.HasValue && RepeatSeconds.HasValue)
                    yield return "Repeat count and repeat seconds can't both be specified.";

                if (FramesPerSecond <= 0m || FramesPerSecond > 100m)
                    yield return "FPS must be greater than 0 and less than or equal to 100.";

                foreach (string light in Lights)
                {
                    if (!IPAddress.TryParse(light, out _))
                        yield return $"{light} is not a valid IP address.";
                }
            }
        }

        static void HandleValidationErrors(IEnumerable<string> errors)
        {
            Console.WriteLine("Please correct the following error(s):");

            foreach (string error in errors)
            {
                Console.WriteLine(" - " + error);
            }

            Environment.ExitCode = -1;
        }

        static async Task Run(Options options, CancellationToken cancellationToken)
        {
            var sequence = Image.Load<Rgb24>(options.Path);
            var lights = options.Lights.Select(IPAddress.Parse)
                .Select((ip, i) => new LifxLight(ip, sequence, i)
                {
                    BrightnessFactor = options.BrightnessFactor
                }).ToArray();

            using (RenderingLoop loop = new RenderingLoop(
                sequence: Image.Load<Rgb24>(options.Path),
                lights: options.Lights
                    .Select(IPAddress.Parse)
                    .Select((ip, i) => new LifxLight(ip, sequence, i) { BrightnessFactor = options.BrightnessFactor })
                    .ToArray(),
                framesPerSecond: options.FramesPerSecond)
            {
                RepeatCount = options.RepeatCount,
                RepeatMilliseconds = options.RepeatSeconds * 1000L,
                SmoothTransitions = options.SmoothTransitions,

                OnRenderingFrame = (frameNumber, repeatNumber, elapsedMilliseconds, loop2) =>
                {
                    string repeatingDescription = loop2.RepeatUntilCancelled ? "until stopped"
                        : loop2.RepeatCount.HasValue ? $"{loop2.RepeatCount - repeatNumber} time(s)"
                        : loop2.RepeatMilliseconds.HasValue ? $"for {(loop2.RepeatMilliseconds - elapsedMilliseconds) / 1000:n0} sec"
                        : throw new NotSupportedException();
                    Console.Write($"\rRendering frame {frameNumber} / {loop2.FrameCount}. Repeating {repeatingDescription}. Press any key to stop...");
                }
            })
            {
                await loop.Start(cancellationToken);
                Console.WriteLine();
            }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    var errors = o.Validate();
                    if (errors.Any())
                    {
                        HandleValidationErrors(errors);
                        return;
                    }

                    CancellationTokenSource cts = new CancellationTokenSource();
                    Task.Run(() => { Console.ReadKey(); cts.Cancel(); });
                    try { Run(o, cts.Token).Wait(); } catch { }
                });
        }
    }
}
