
import { useState, useEffect } from 'react';
import { Plus, Trash2, Edit2, Command, MessageSquare, Zap, Bot, Wand2 } from 'lucide-react';
// Imports for file system removed as we switched to localStorage for MVP persistence


export interface PromptTemplate {
    id: string;
    name: string;
    description: string;
    systemPrompt: string;
    icon: 'command' | 'message' | 'zap' | 'bot' | 'wand';
    isDefault?: boolean;
}

const DEFAULT_TEMPLATES: PromptTemplate[] = [
    {
        id: 'fix_grammar',
        name: 'Fix Grammar',
        description: 'Corrects grammar and removes filler words.',
        systemPrompt: 'You are a professional editor. Correct the grammar, punctuation, and flow of the text. Remove filler words like "um", "uh". Do not change the meaning. Output only the corrected text.',
        icon: 'wand',
        isDefault: true
    },
    {
        id: 'summarize',
        name: 'Summarize',
        description: 'Create a concise bullet-point summary.',
        systemPrompt: 'Summarize the following transcript into concise bullet points. Capture key action items and decisions.',
        icon: 'zap',
        isDefault: true
    },
    {
        id: 'professional_email',
        name: 'Professional Email',
        description: 'Convert into a professional email draft.',
        systemPrompt: 'Draft a professional email based on this stream of consciousness. Use a polite and clear tone. Output the email subject and body.',
        icon: 'message',
        isDefault: true
    }
];

export function ModesView() {
    const [templates, setTemplates] = useState<PromptTemplate[]>([]);
    // const [loading, setLoading] = useState(true); // Unused for now as we load from localStorage synchronously or fast enough
    const [isEditing, setIsEditing] = useState(false);
    const [editTemplate, setEditTemplate] = useState<PromptTemplate | null>(null);

    useEffect(() => {
        loadTemplates();
    }, []);

    const loadTemplates = async () => {
        try {
            // Try to load from local storage first to be simple, or FS if we want more persistence across updates
            // For now, let's use localStorage for simplicity as per plan "Move AUTO_MODE_RULES to persistent storage"
            // But here we are doing Templates.
            const saved = localStorage.getItem('elite_whisper_prompt_templates');
            if (saved) {
                setTemplates(JSON.parse(saved));
            } else {
                setTemplates(DEFAULT_TEMPLATES);
            }
        } catch (e) {
            console.error("Failed to load templates", e);
            setTemplates(DEFAULT_TEMPLATES);
        } finally {
            // setLoading(false);
        }
    };

    const saveTemplates = (newTemplates: PromptTemplate[]) => {
        setTemplates(newTemplates);
        localStorage.setItem('elite_whisper_prompt_templates', JSON.stringify(newTemplates));
    };

    const handleSaveEdit = () => {
        if (!editTemplate) return;

        const newTemplates = templates.map(t => t.id === editTemplate.id ? editTemplate : t);
        if (!templates.find(t => t.id === editTemplate.id)) {
            newTemplates.push(editTemplate);
        }

        saveTemplates(newTemplates);
        setIsEditing(false);
        setEditTemplate(null);
    };

    const handleDelete = (id: string) => {
        const newTemplates = templates.filter(t => t.id !== id);
        saveTemplates(newTemplates);
    };

    const createNew = () => {
        setEditTemplate({
            id: crypto.randomUUID(),
            name: 'New Template',
            description: 'Description...',
            systemPrompt: 'You are a helpful assistant...',
            icon: 'bot',
            isDefault: false
        });
        setIsEditing(true);
    };

    // Icons map
    const IconMap = { command: Command, message: MessageSquare, zap: Zap, bot: Bot, wand: Wand2 };

    if (isEditing && editTemplate) {
        return (
            <div className="flex-1 bg-white p-8 overflow-y-auto">
                <div className="max-w-3xl mx-auto">
                    <div className="flex items-center justify-between mb-8">
                        <h2 className="text-2xl font-bold text-gray-800">Edit Template</h2>
                        <div className="flex gap-2">
                            <button onClick={() => setIsEditing(false)} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg">Cancel</button>
                            <button onClick={handleSaveEdit} className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 font-medium">Save Template</button>
                        </div>
                    </div>

                    <div className="space-y-6">
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Name</label>
                            <input
                                type="text"
                                value={editTemplate.name}
                                onChange={e => setEditTemplate({ ...editTemplate, name: e.target.value })}
                                className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 outline-none"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Description</label>
                            <input
                                type="text"
                                value={editTemplate.description}
                                onChange={e => setEditTemplate({ ...editTemplate, description: e.target.value })}
                                className="w-full px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 outline-none"
                            />
                        </div>
                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">Icon</label>
                            <div className="flex gap-4">
                                {(Object.keys(IconMap) as Array<keyof typeof IconMap>).map(iconKey => {
                                    const Icon = IconMap[iconKey];
                                    return (
                                        <button
                                            key={iconKey}
                                            onClick={() => setEditTemplate({ ...editTemplate, icon: iconKey })}
                                            className={`p-3 rounded-xl border transition-all ${editTemplate.icon === iconKey ? 'bg-indigo-50 border-indigo-500 text-indigo-600' : 'border-gray-200 hover:bg-gray-50'}`}
                                        >
                                            <Icon size={20} />
                                        </button>
                                    )
                                })}
                            </div>
                        </div>

                        <div>
                            <label className="block text-sm font-medium text-gray-700 mb-1">System Prompt</label>
                            <p className="text-xs text-gray-500 mb-2">Instructions for the AI on how to process the transcript.</p>
                            <textarea
                                value={editTemplate.systemPrompt}
                                onChange={e => setEditTemplate({ ...editTemplate, systemPrompt: e.target.value })}
                                className="w-full h-40 px-4 py-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-indigo-500 outline-none font-mono text-sm"
                            />
                        </div>
                    </div>
                </div>
            </div>
        )
    }

    return (
        <div className="flex-1 flex flex-col h-full bg-gray-50 overflow-y-auto">
            <div className="bg-white border-b border-gray-200 px-8 py-6 sticky top-0 z-10">
                <div className="flex justify-between items-center">
                    <div>
                        <h1 className="text-2xl font-bold text-gray-800 flex items-center gap-2">
                            <Wand2 className="text-indigo-600" /> Post-Processing Templates
                        </h1>
                        <p className="text-gray-500 mt-1">Manage AI prompts to transform your transcripts.</p>
                    </div>
                    <button
                        onClick={createNew}
                        className="px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 font-bold flex items-center gap-2 shadow-sm transition-all active:scale-95"
                    >
                        <Plus size={18} /> New Template
                    </button>
                </div>
            </div>

            <div className="p-8 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6 max-w-7xl mx-auto w-full">
                {templates.map(template => {
                    const Icon = IconMap[template.icon] || Wand2;
                    return (
                        <div key={template.id} className="bg-white p-6 rounded-2xl border border-gray-200 shadow-sm hover:shadow-md transition-shadow group flex flex-col h-full">
                            <div className="flex items-start justify-between mb-4">
                                <div className="p-3 bg-indigo-50 text-indigo-600 rounded-xl">
                                    <Icon size={24} />
                                </div>
                                <div className="flex gap-2 opacity-0 group-hover:opacity-100 transition-opacity">
                                    <button
                                        onClick={() => { setEditTemplate(template); setIsEditing(true); }}
                                        className="p-2 text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors"
                                    >
                                        <Edit2 size={16} />
                                    </button>
                                    {!template.isDefault && (
                                        <button
                                            onClick={() => handleDelete(template.id)}
                                            className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                    )}
                                </div>
                            </div>

                            <h3 className="text-lg font-bold text-gray-800 mb-2">{template.name}</h3>
                            <p className="text-sm text-gray-600 mb-4 line-clamp-2">{template.description}</p>

                            <div className="mt-auto pt-4 border-t border-gray-100">
                                <p className="text-xs text-gray-400 font-mono truncate bg-gray-50 p-2 rounded">
                                    {template.systemPrompt}
                                </p>
                            </div>
                        </div>
                    );
                })}

                <button
                    onClick={createNew}
                    className="border-2 border-dashed border-gray-200 rounded-2xl p-6 flex flex-col items-center justify-center text-gray-400 hover:border-indigo-300 hover:text-indigo-500 hover:bg-indigo-50/10 transition-all gap-3 min-h-[200px]"
                >
                    <Plus size={32} />
                    <span className="font-semibold">Create Custom Template</span>
                </button>
            </div>
        </div>
    );
}

