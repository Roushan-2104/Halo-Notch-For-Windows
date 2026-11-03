# Third-party notices

Shammy itself is MIT-licensed (see [LICENSE](LICENSE)). It downloads the following speech models on first
use; they are not part of the installer and keep their own licenses.

## OpenAI Whisper models (MIT)

The base, small and large-v3-turbo voice models are OpenAI's Whisper models in the GGML format published by
the whisper.cpp project:

- Models: https://huggingface.co/ggerganov/whisper.cpp
- Original models and paper: https://github.com/openai/whisper

Copyright (c) 2022 OpenAI. Released under the MIT License:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
> associated documentation files (the "Software"), to deal in the Software without restriction, including
> without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
> following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or substantial
> portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
> LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
> EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
> IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE
> USE OR OTHER DEALINGS IN THE SOFTWARE.

## Whisper-Hindi2Hinglish-Apex by Oriserve (Apache-2.0)

The optional *Hinglish voice* uses Oriserve's Whisper-Hindi2Hinglish-Apex, a fine-tune of Whisper
large-v3-turbo:

- Model card: https://huggingface.co/Oriserve/Whisper-Hindi2Hinglish-Apex
- Revision used: `f3214eed20b4e4d4144e739982d911f87b9cb223`
- License: Apache License 2.0, full text in [LICENSES/Apache-2.0.txt](LICENSES/Apache-2.0.txt) (also https://www.apache.org/licenses/LICENSE-2.0). The upstream repository has no NOTICE file.

**Modification:** Shammy converted the model to the GGML format used by whisper.cpp and quantized it to
q5_0, giving `ggml-apex-hinglish-q5_0.bin` (SHA-256
`9d877151b15cec1feb9110cfbc0a3162cf377bcc0ab1935174226f461cf60f13`). It is published as an asset of the
`voice-models-1` pre-release of this repository, unchanged otherwise. The weights are Oriserve's work and
remain under the Apache License 2.0; Shammy's MIT license doesn't apply to them.


## Schibsted Grotesk (SIL Open Font License 1.1)

Shammy's interface type. Copyright 2023 The Schibsted-Grotesk Project Authors
(https://github.com/schibsted/schibsted-grotesk). Shipped as static weights (Regular, Medium, SemiBold, Bold,
ExtraBold) instanced from the variable font; licence text in [LICENSES/OFL-SchibstedGrotesk.txt](LICENSES/OFL-SchibstedGrotesk.txt).
