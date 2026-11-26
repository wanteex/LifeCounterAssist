import { COMMANDER_ART_PREFIX, type Commander } from './types'

const SEARCH_URL = 'https://api.scryfall.com/cards/search'
const MAX_RESULTS = 8

interface ScryfallImages {
  art_crop?: string
}

interface ScryfallCard {
  name: string
  artist?: string
  image_uris?: ScryfallImages
  card_faces?: { artist?: string; image_uris?: ScryfallImages }[]
}

/** Searches cards that can be commanders, most played first. Empty list when nothing matches. */
export async function searchCommanders(text: string, signal: AbortSignal): Promise<Commander[]> {
  const q = encodeURIComponent(`${text} is:commander`)
  const response = await fetch(`${SEARCH_URL}?q=${q}&order=edhrec`, { signal, headers: { Accept: 'application/json' } })
  // Scryfall answers 404 when a search has no results.
  if (response.status === 404) return []
  if (!response.ok) throw new Error(`scryfall_${response.status}`)
  const body = (await response.json()) as { data?: ScryfallCard[] }
  const results: Commander[] = []
  for (const card of body.data ?? []) {
    const face = card.card_faces?.[0]
    const artUrl = card.image_uris?.art_crop ?? face?.image_uris?.art_crop
    // Anything the server would reject would block joining: skip it.
    if (!artUrl?.startsWith(COMMANDER_ART_PREFIX)) continue
    results.push({ name: card.name, artUrl, artist: card.artist ?? face?.artist ?? '' })
    if (results.length === MAX_RESULTS) break
  }
  return results
}
