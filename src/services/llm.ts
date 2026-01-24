
export interface LLMRequest {
    systemPrompt: string;
    userPrompt: string;
    model?: string;
}

export interface LLMResponse {
    text: string;
    error?: string;
}

export abstract class LLMProvider {
    abstract generate(req: LLMRequest): Promise<LLMResponse>;
}

export class OpenAIProvider extends LLMProvider {
    constructor(private apiKey: string) { super(); }

    async generate(req: LLMRequest): Promise<LLMResponse> {
        try {
            const res = await fetch('https://api.openai.com/v1/chat/completions', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${this.apiKey}`
                },
                body: JSON.stringify({
                    model: req.model || 'gpt-4o', // Default to 4o
                    messages: [
                        { role: 'system', content: req.systemPrompt },
                        { role: 'user', content: req.userPrompt }
                    ]
                })
            });

            if (!res.ok) {
                const err = await res.json();
                return { text: '', error: err.error?.message || 'OpenAI Error' };
            }

            const data = await res.json();
            return { text: data.choices[0].message.content };
        } catch (e: any) {
            return { text: '', error: e.message };
        }
    }
}

export class AnthropicProvider extends LLMProvider {
    constructor(private apiKey: string) { super(); }

    async generate(req: LLMRequest): Promise<LLMResponse> {
        try {
            // Anthropic is tricky from browser due to CORS usually, but if we run in Tauri, 
            // the CSP might block it or we need the Rust Proxy. 
            // For now, let's try direct fetch. If CORS fails, we'll need a Rust command proxy.
            const res = await fetch('https://api.anthropic.com/v1/messages', {
                method: 'POST',
                headers: {
                    'x-api-key': this.apiKey,
                    'anthropic-version': '2023-06-01',
                    'content-type': 'application/json',
                    'dangerously-allow-browser-utils': 'true' // Only for dev, ideally use backend
                },
                body: JSON.stringify({
                    model: req.model || 'claude-3-5-sonnet-20240620',
                    max_tokens: 4096,
                    system: req.systemPrompt,
                    messages: [{ role: 'user', content: req.userPrompt }]
                })
            });

            if (!res.ok) {
                const err = await res.json();
                return { text: '', error: err.error?.message || 'Anthropic Error' };
            }

            const data = await res.json();
            return { text: data.content[0].text };
        } catch (e: any) {
            return { text: '', error: e.message };
        }
    }
}

export class LocalProvider extends LLMProvider {
    constructor(private baseUrl: string = 'http://localhost:11434/v1', private model: string = 'llama3') { super(); }

    async generate(req: LLMRequest): Promise<LLMResponse> {
        try {
            // Support Ollama / OpenAI-Compatible endpoints
            // Often /v1/chat/completions
            const url = this.baseUrl.endsWith('/') ? `${this.baseUrl}chat/completions` : `${this.baseUrl}/chat/completions`;

            const res = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    model: this.model, // Ollama needs model name, usually ignored by others or required
                    messages: [
                        { role: 'system', content: req.systemPrompt },
                        { role: 'user', content: req.userPrompt }
                    ],
                    stream: false
                })
            });

            if (!res.ok) {
                return { text: '', error: `Local Error: ${res.statusText}` };
            }

            const data = await res.json();
            return { text: data.choices[0].message.content };
        } catch (e: any) {
            return { text: '', error: `Connection Refused: ${this.baseUrl}. Is Ollama running?` };
        }
    }
}

export function getLLMProvider(): LLMProvider | null {
    // Priority: OpenAI -> Anthropic -> Local? 
    // Or we should let user choose "Active Provider".
    // For MVP transparency: Check keys.

    // Ideally we add an "Active Provider" selector in Settings. 
    // For now: Local has priority if set (privacy first), then OpenAI, then Anthropic.

    const localUrl = localStorage.getItem('elite_whisper_local_url');
    if (localUrl) {
        const localModel = localStorage.getItem('elite_whisper_local_model') || 'llama3';
        return new LocalProvider(localUrl, localModel);
    }

    const openAIKey = localStorage.getItem('elite_whisper_openai_key');
    if (openAIKey) return new OpenAIProvider(openAIKey);

    const anthropicKey = localStorage.getItem('elite_whisper_anthropic_key');
    if (anthropicKey) return new AnthropicProvider(anthropicKey);

    return null;
}
