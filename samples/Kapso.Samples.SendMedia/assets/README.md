# Sample media

Generated with ffmpeg, committed so the sample runs without it.

```bash
ffmpeg -f lavfi -i "testsrc2=size=480x480:rate=1" -frames:v 1 sample-image.png

ffmpeg -f lavfi -i "sine=frequency=440:duration=2:sample_rate=48000" \
       -ac 1 -c:a libopus -b:a 32k sample-voice.ogg
```

The audio matters more than it looks. WhatsApp accepts `audio/ogg` **only** when
the codec is Opus, and only Opus renders as a voice note rather than as an
attached file. A plain Ogg Vorbis file is rejected.
