WHAT NEEDS SOURCE? WHAT IS SOURCE?

First of all:
- "Source", as required by the GPL, is the "preferred form for making
  modifications" of a file.
- Thus, a file that by itself is already the preferred form to edit the file,
  is considered its own source in the scope of the GPL.
- The source of a generated file, however, is typically the input to the
  generator.
- Files that contain the same information, and can be converted without any
  noticeable loss of information, are equivalent. So if you e.g. made a texture
  as BMP file and saved it as TGA for the game, clearly the BMP is not
  required. Also, a HIGH QUALITY JPEG file is usually good enough, as long as
  it would be good enough for you making modifications to it too.
- There is no requirement that the "source files" must be useful for anyone
  other than you. If they only work on a now-dead computer platform, so be it.
  If they only work in a really expensive commercial application, so be it.
  However, they still must be complete (as in, you must not be "hiding" data on
  your own system - note that this does NOT aim to prevent use of commercial
  plugins/additions for the software being used), so that the source files plus
  the parts needed to read it can be turned into the output file again.
- Just be honest. You know best what file you use to make modifications, and
  should simply provide that, whatever it is. We may be unable to verify it
  fully, but we will reject submissions of which we know that we cannot obtain
  the complete source.


Specifics:

models/
If you have a file that contains more information than the .smd files the
engine can export from the various files, it needs to be provided. Often
however, the data in the .smd is already complete and more is not needed.
.blend files are always nice though, as Blender can't deal well with .smd
files.

env/
Photographed skyboxes need no extra source. Generated skyboxes using 3D
software (or Terragen) need the project file.

gfx/, textures/
2D art generally needs no extra source as it is perfectly editable on its own,
and typically not generated. In case a layered source file (XCF, PSD) exists,
providing that would be very nice, though.

sound/
Recorded sound effects generally need no extra source. Generated sound effects
need source (the input file for the generator).

sound/cdtracks/
Synthetic music needs some machine readable equivalent to musical score as
source. Typically this would be the project file of the application used to
create it. Commercial synthesis software may of course be used anyway.
Recorded music (using real instruments) needs no source, but musical score in a
usable form (e.g. file of a notation application, or if the music was just
handwritten, a scan) would be nice.
