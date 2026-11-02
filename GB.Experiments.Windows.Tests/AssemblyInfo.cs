using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize] // Native UI tests share the desktop and process message loops.
