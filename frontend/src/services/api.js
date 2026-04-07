const API_URL = import.meta.env.VITE_API_URL || 'https://app-pseg-main-eus2-mx01.azurewebsites.net/api/chat';

export async function sendMessage(query, conversationId) {
  const res = await fetch(API_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ query, conversationId: conversationId || undefined }),
  });

  if (!res.ok) {
    const text = await res.text();
    throw new Error(`Chat request failed (${res.status}): ${text}`);
  }

  return res.json();
}
