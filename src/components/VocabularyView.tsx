import { useState, useEffect } from 'react';
import { invoke } from '@tauri-apps/api/core';
import { Plus, Trash2, Save, Book } from 'lucide-react';

export function VocabularyView() {
    const [words, setWords] = useState<string[]>([]);
    const [newWord, setNewWord] = useState('');
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    const [status, setStatus] = useState<string | null>(null);

    useEffect(() => {
        loadVocabulary();
    }, []);

    const loadVocabulary = async () => {
        try {
            const vocab = await invoke<string[]>('cmd_get_vocabulary');
            setWords(vocab);
        } catch (error) {
            console.error('Failed to load vocabulary:', error);
            setStatus('Failed to load words');
        } finally {
            setLoading(false);
        }
    };

    const handleAddWord = async (e?: React.FormEvent) => {
        if (e) e.preventDefault();

        const trimmed = newWord.trim();
        if (!trimmed) return;

        if (words.includes(trimmed)) {
            setStatus('Word already exists');
            setTimeout(() => setStatus(null), 2000);
            return;
        }

        const updated = [...words, trimmed];
        setWords(updated);
        setNewWord('');
        await saveVocabulary(updated);
    };

    const handleDeleteWord = async (wordToDelete: string) => {
        const updated = words.filter(w => w !== wordToDelete);
        setWords(updated);
        await saveVocabulary(updated);
    };

    const saveVocabulary = async (updatedWords: string[]) => {
        setSaving(true);
        setStatus('Saving...');
        try {
            await invoke('cmd_save_vocabulary', { words: updatedWords });
            setStatus('Saved!');
            setTimeout(() => setStatus(null), 2000);
        } catch (error) {
            console.error('Failed to save vocabulary:', error);
            setStatus('Failed to save');
        } finally {
            setSaving(false);
        }
    };

    return (
        <div className="flex-1 flex flex-col h-full bg-[#f9f9f9] overflow-hidden">
            {/* Header */}
            <div className="px-8 py-6 border-b border-gray-200 bg-white flex justify-between items-center">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900 flex items-center gap-2">
                        <Book className="w-6 h-6 text-blue-500" />
                        Custom Vocabulary
                    </h1>
                    <p className="text-gray-500 text-sm mt-1">
                        Add names, acronyms, or specific jargon to improve recognition accuracy.
                    </p>
                </div>
                {status && (
                    <div className={`px-3 py-1 rounded-full text-xs font-semibold animate-fade-in
                        ${status.includes('Failed') ? 'bg-red-100 text-red-700' : 'bg-green-100 text-green-700'}`}>
                        {status}
                    </div>
                )}
            </div>

            {/* Content */}
            <div className="flex-1 overflow-y-auto p-8">
                <div className="max-w-2xl mx-auto space-y-6">

                    {/* Add New Word Card */}
                    <div className="bg-white p-6 rounded-xl shadow-sm border border-gray-200">
                        <label className="block text-sm font-medium text-gray-700 mb-2">Add New Word or Phrase</label>
                        <form onSubmit={handleAddWord} className="flex gap-2">
                            <input
                                type="text"
                                value={newWord}
                                onChange={(e) => setNewWord(e.target.value)}
                                placeholder="e.g. 'Tauri', 'RustLang', 'MyCompany'"
                                className="flex-1 px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-blue-500 outline-none transition-all"
                            />
                            <button
                                type="submit"
                                disabled={!newWord.trim() || saving}
                                className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition-colors flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                <Plus size={18} />
                                Add
                            </button>
                        </form>
                    </div>

                    {/* Word List */}
                    <div className="bg-white rounded-xl shadow-sm border border-gray-200 overflow-hidden">
                        <div className="px-6 py-4 border-b border-gray-100 bg-gray-50 flex justify-between items-center">
                            <h3 className="font-semibold text-gray-700">Your Dictionary ({words.length})</h3>
                            {saving && <span className="text-xs text-gray-400 flex items-center gap-1"><Save size={12} /> Syncing...</span>}
                        </div>

                        {loading ? (
                            <div className="p-8 text-center text-gray-500">Loading...</div>
                        ) : words.length === 0 ? (
                            <div className="p-12 text-center text-gray-400">
                                <Book size={48} className="mx-auto mb-3 opacity-20" />
                                <p>No custom words added yet.</p>
                            </div>
                        ) : (
                            <div className="divide-y divide-gray-100">
                                {words.map((word, index) => (
                                    <div key={index} className="px-6 py-3 flex justify-between items-center hover:bg-gray-50 transition-colors group">
                                        <span className="text-gray-800 font-medium">{word}</span>
                                        <button
                                            onClick={() => handleDeleteWord(word)}
                                            disabled={saving}
                                            className="text-gray-300 hover:text-red-500 p-2 rounded-full hover:bg-red-50 transition-all opacity-0 group-hover:opacity-100"
                                            title="Delete Word"
                                        >
                                            <Trash2 size={16} />
                                        </button>
                                    </div>
                                ))}
                            </div>
                        )}
                    </div>

                    <div className="p-4 bg-blue-50 text-blue-800 rounded-lg text-sm border border-blue-100">
                        <strong>Tip:</strong> Changes are auto-saved. The AI model will use these words to bias the transcription. This works best for words that sound similar to common words but have unique spelling.
                    </div>
                </div>
            </div>
        </div>
    );
}
