# Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
# Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

# Emscripten port of Stride's native audio library (libstrideaudio): the OpenAL layer and the CELT codec of
# sources/plugins/Audio/Stride.Audio/Native, with the CELT part of Opus 1.1.3 that the other platforms link as libCelt.
# Use with --use-port=<this file> -lopenal: OpenAL is the one of Emscripten, over Web Audio.

import os

OPUS_TAG = 'v1.1.3'
OPUS_HASH = '34512896814c0c0e1b5f690c8833e9d7e111021a5e4cdc98bdcce12394fc5b429322c1e6576de50c47773e1ff17b2c083abb497d772a15ee90181044fd6f439d'

# The name Emscripten gives a port loaded from a file: its projects are fetched and cleared under it
NAME = os.path.basename(__file__)[:-len('.py')]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
NATIVE = os.path.join(ROOT, 'sources', 'plugins', 'Audio', 'Stride.Audio', 'Native')

CELT_SOURCES = ['bands.c', 'celt.c', 'celt_encoder.c', 'celt_decoder.c', 'cwrs.c', 'entcode.c', 'entdec.c', 'entenc.c',
                'kiss_fft.c', 'laplace.c', 'mathops.c', 'mdct.c', 'modes.c', 'pitch.c', 'celt_lpc.c', 'quant_bands.c',
                'rate.c', 'vq.c']

# The Stride sources stay where they are: each is compiled through a file of the port that includes it, so that the
# objects land in the build folder of the port. Their relative includes resolve from their own folder.
STRIDE_SOURCES = {
  'stride_openal.cpp': os.path.join(NATIVE, 'OpenAL.cpp'),
  'stride_celt.cpp': os.path.join(NATIVE, 'Celt.cpp'),
  'stride_openal_symbols.c': os.path.join(NATIVE, 'OpenALSymbols.Emscripten.c'),
  'stride_celt_extras.c': os.path.join(ROOT, 'deps', 'Celt', 'celt_extras.c'),
}


def get_lib_names():
  return ['libstride-celt.a', 'libstride-audio.a']


def get(ports, settings, shared):
  ports.fetch_project(NAME, f'https://github.com/xiph/opus/archive/{OPUS_TAG}.zip', sha512hash=OPUS_HASH)
  opus = ports.get_dir(NAME, 'opus-' + OPUS_TAG[1:])
  codec_flags = ['-DOPUS_BUILD', '-DCUSTOM_MODES', '-DUSE_ALLOCA', '-DHAVE_LRINTF', '-DHAVE_LRINT',
                 '-I' + os.path.join(opus, 'include'), '-I' + os.path.join(opus, 'celt')]

  def create_celt(final):
    ports.build_port(opus, final, 'stride-celt', flags=codec_flags, srcs=[os.path.join('celt', s) for s in CELT_SOURCES])

  def create_audio(final):
    staging = ports.get_dir(NAME, 'stride')
    for name, source in STRIDE_SOURCES.items():
      with open(os.path.join(staging, name), 'w') as wrapper:
        wrapper.write('#include "%s"\n' % source.replace('\\', '/'))
    flags = [
      # The sources include the headers of deps by paths relative to a folder two or three levels deep
      '-I' + os.path.join(ROOT, 'sources', 'engine'),
      '-I' + os.path.join(ROOT, 'sources', 'engine', 'Stride.Native'),
      '-I' + os.path.join(ROOT, 'deps', 'NativePath'),
      # They are written against the C library declarations of NativePath, not against those of the system
      '-nostdinc', '-nostdinc++', '-I' + os.path.join(ROOT, 'deps', 'NativePath', 'standard'),
      # Those headers define again macros the compiler predefines; ports build with warnings as errors
      '-Wno-error',
      '-I' + os.path.join(ROOT, 'deps', 'Celt', 'include'),
      '-fno-exceptions', '-fno-rtti',
    ]
    ports.build_port(staging, final, 'stride-audio', flags=flags, srcs=list(STRIDE_SOURCES.keys()))

  return [shared.cache.get_lib('libstride-audio.a', create_audio, what='port'),
          shared.cache.get_lib('libstride-celt.a', create_celt, what='port')]


def clear(ports, settings, shared):
  for name in get_lib_names():
    shared.cache.erase_lib(name)


def process_args(ports):
  return []


def show():
  return 'stride-audio (Stride native audio layer, MIT license; Opus/CELT, BSD license)'
