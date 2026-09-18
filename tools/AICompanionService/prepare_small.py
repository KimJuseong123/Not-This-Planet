import os
from huggingface_hub import snapshot_download
from engine import ROOT

if __name__ == '__main__':
    os.environ['HF_HUB_DISABLE_SYMLINKS_WARNING'] = '1'
    path = snapshot_download('Systran/faster-whisper-small', local_dir=ROOT/'models'/'whisper-small',
        allow_patterns=['config.json','model.bin','tokenizer.json','vocabulary.*','preprocessor_config.json'])
    print('Whisper small ready:', path, flush=True)
