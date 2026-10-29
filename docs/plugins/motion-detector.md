# Motion Detector

Build Plugins/SkyCD.Plugin.MotionDetector and install its assembly through the normal plugin loader. Discovery uses assembly capabilities; no plugin.json is required.

Install the external capture application beside the plugin at motion/motion.exe. SkyCD does not bundle this executable. Tools > Motion Detector starts it with the motion directory as its working directory. Close the capture application to finish and import; invoke a plugin command again to cancel the active capture. Cancellation terminates the launched process tree and does not import an incomplete session.

Only files first observed during capture are imported. AVI/MPG/WMV go under Movies/yyyy-MM-dd; JPG/BMP under Pictures/yyyy-MM-dd, using the local detection date. Existing files and other extensions are ignored. Metadata preserves Name, Size, LastWriteTime, LastAccessTime, IsReadOnly, Attributes, CreationTime and FullName. Size is normalized to bytes to match the current catalog; legacy Length * 8 bit semantics are deliberately not retained.

ICatalogCaptureHostApi is an optional extension of IHostCommandApi. Older menu plugins remain compatible. The desktop implementation captures the current catalog root, writes all folders and files in one transaction, and refreshes the tree and list after success. Missing executable, launch, file access and import failures appear in the host status notification.
