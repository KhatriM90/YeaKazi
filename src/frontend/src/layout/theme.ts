export type ThemeChoice = 'system' | 'light' | 'dark'

const KEY = 'yeakazi.theme'

export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement
  if (choice === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', choice)
  localStorage.setItem(KEY, choice)
}

export function savedTheme(): ThemeChoice {
  const v = localStorage.getItem(KEY)
  return v === 'light' || v === 'dark' ? v : 'system'
}
