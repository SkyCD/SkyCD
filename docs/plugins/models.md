# 3D model metadata and thumbnails

The `SkyCD.Plugin.Metadata.Models` assembly is discovered as `SkyCD.Plugin.Models`. It publishes
`skycd.models.metadata` v1 and the index-time `skycd.models.preview` v1 schema at priority 10.

| Files | Metadata and preview support |
| --- | --- |
| glTF and GLB | Asset version, generator, extensions, scene and geometry counts, embedded buffer status, scene-space geometry and material base colour |
| OBJ | Objects, groups, material uses and library names, vertices and polygon faces |
| STL | ASCII and binary headers, triangles and stored vertices |
| PLY | ASCII and both binary byte orders, comments, element counts, vertices and faces |
| OFF | ASCII vertices, polygon faces and comments |

Bounds are text triplets in source units. Invalid data publishes a failure code. `ScanIncomplete`
marks a source or geometry scan that reached its configured limit. OBJ, STL, PLY and OFF reads are
limited to 8 MiB. glTF and GLB reads are limited to 16 MiB. Larger GLBs read up to 4 MiB for the
JSON header. OBJ, OFF and PLY faces are fan-triangulated.

Thumbnail PNGs use a fixed orthographic view, flat shading and a transparent background. They
are limited to 512 pixels per edge, 4 MiB, 50,000 triangles and 200,000 vertices. SharpGLTF.Core
decodes embedded glTF geometry and SkiaSharp draws the thumbnail in software. Referenced
resource paths are never opened. Models using external glTF buffers or images, Draco compression
or vertex-only geometry can still supply metadata.
