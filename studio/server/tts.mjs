// Synteza mowy z czasami słów (do napisów i ruchu ust).
// voice: {provider: "Azure"|"ElevenLabs", voiceId, rate, pitch, model, extra:{...}}
// credentials: {azureKey, azureRegion, elevenLabsKey}
// Zwraca {audio: Buffer (mp3), words: [{text,startMs,endMs}], durationMs}

const PERMISSIONS = {
  text_to_speech: "Text to Speech",
  voices_read: "Voices: Read",
  sound_generation: "Sound Effects",
  music_generation: "Music Generation",
};

/** Czytelny komunikat zamiast surowej odpowiedzi ElevenLabs (np. brak uprawnienia klucza). */
export function elevenLabsError(status, body) {
  if (body.includes("api_key_id_used_as_api_key"))
    return "W Studiu jest zapisany identyfikator klucza ElevenLabs zamiast samego klucza (prawdziwy zaczyna się od sk_).";
  const missing = /missing the permission (\w+)/.exec(body)?.[1];
  if (missing)
    return `Klucz ElevenLabs nie ma uprawnienia „${PERMISSIONS[missing] ?? missing}”. Włącz je w ElevenLabs → Developers → API Keys → edycja klucza (dla Studia: Text to Speech, Voices: Read, Sound Effects, Music Generation).`;
  if (status === 401) return "ElevenLabs odrzucił klucz — sprawdź, czy jest aktualny (Studio reklam → Głos i dźwięk).";
  if (status === 429) return "ElevenLabs: wyczerpany limit znaków albo za dużo zapytań naraz — sprawdź plan konta.";
  return `ElevenLabs ${status}: ${body.slice(0, 200)}`;
}

const xml = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

export async function synthesize(voice, credentials, text) {
  if (!text?.trim()) throw Object.assign(new Error("Pusty tekst sceny"), { status: 400 });
  const provider = String(voice?.provider ?? "Azure").toLowerCase();
  if (provider === "azure") return azure(voice, credentials, text);
  if (provider === "elevenlabs") return elevenLabs(voice, credentials, text);
  throw Object.assign(new Error(`Nieznany dostawca głosu: ${voice?.provider}`), { status: 400 });
}

async function azure(voice, { azureKey, azureRegion } = {}, text) {
  if (!azureKey || !azureRegion) throw Object.assign(new Error("Brak klucza lub regionu Azure Speech (Ustawienia → Głos)"), { status: 400 });
  const sdk = (await import("microsoft-cognitiveservices-speech-sdk")).default;
  const cfg = sdk.SpeechConfig.fromSubscription(azureKey, azureRegion);
  cfg.speechSynthesisOutputFormat = sdk.SpeechSynthesisOutputFormat.Audio24Khz96KBitRateMonoMp3;
  const synth = new sdk.SpeechSynthesizer(cfg, null);
  const words = [];
  synth.wordBoundary = (_, e) => {
    if (e.boundaryType !== sdk.SpeechSynthesisBoundaryType.Word) return;
    words.push({
      text: e.text,
      startMs: Math.round(e.audioOffset / 10000),
      endMs: Math.round((e.audioOffset + e.duration) / 10000),
    });
  };
  const ssml = `<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="pl-PL">
<voice name="${xml(voice.voiceId)}"><prosody rate="${xml(voice.rate || "+0%")}" pitch="${xml(voice.pitch || "+0Hz")}">${xml(text)}</prosody></voice></speak>`;
  try {
    const result = await new Promise((res, rej) => synth.speakSsmlAsync(ssml, res, rej));
    if (result.reason !== sdk.ResultReason.SynthesizingAudioCompleted)
      throw new Error(`Azure TTS: ${result.errorDetails ?? result.reason}`);
    return { audio: Buffer.from(result.audioData), words, durationMs: Math.round(result.audioDuration / 10000) };
  } finally {
    synth.close();
  }
}

async function elevenLabs(voice, { elevenLabsKey } = {}, text) {
  if (!elevenLabsKey) throw Object.assign(new Error("Brak klucza ElevenLabs (Ustawienia → Głos)"), { status: 400 });
  const extra = voice.extra ?? {};
  const speed = Number.parseFloat(voice.rate);
  const body = {
    text,
    model_id: voice.model || "eleven_multilingual_v2",
    language_code: "pl",
    voice_settings: {
      stability: extra.stability ?? 0.5,
      similarity_boost: extra.similarity_boost ?? 0.75,
      style: extra.style ?? 0.15,
      use_speaker_boost: true,
      ...(Number.isFinite(speed) && speed > 0.6 && speed < 1.3 ? { speed } : {}),
    },
  };
  const url = `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voice.voiceId)}/with-timestamps?output_format=mp3_44100_128`;
  const res = await fetch(url, {
    method: "POST",
    headers: { "xi-api-key": elevenLabsKey, "content-type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw Object.assign(new Error(elevenLabsError(res.status, await res.text())), { status: 400 });
  const data = await res.json();
  const audio = Buffer.from(data.audio_base64, "base64");
  const a = data.alignment ?? data.normalized_alignment;
  const words = [];
  let cur = null;
  a?.characters.forEach((ch, i) => {
    const isWordChar = /[\p{L}\p{N}'’-]/u.test(ch);
    if (isWordChar) {
      if (!cur) cur = { text: "", startMs: Math.round(a.character_start_times_seconds[i] * 1000), endMs: 0 };
      cur.text += ch;
      cur.endMs = Math.round(a.character_end_times_seconds[i] * 1000);
    } else if (cur) {
      words.push(cur);
      cur = null;
    }
  });
  if (cur) words.push(cur);
  const durationMs = Math.round((audio.length * 8) / 128); // mp3 128 kbps CBR
  return { audio, words, durationMs };
}
