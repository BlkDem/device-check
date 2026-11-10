import { describe, expect, it } from 'vitest'
import { AGENT_STEPS, parseAgentHash } from '../lib/agent'

const TOKEN = 'Xk3_9fQ-2bLmN7pRsT1uVwY4zA6cD8eF0gH5jK2lMn0'

describe('parseAgentHash', () => {
  it('reads port and token from the tray link fragment', () => {
    expect(parseAgentHash(`#agent=47615&token=${TOKEN}`)).toEqual({ port: 47615, token: TOKEN })
    expect(parseAgentHash(`agent=50123&token=${TOKEN}`)).toEqual({ port: 50123, token: TOKEN })
  })

  it('rejects anything malformed', () => {
    expect(parseAgentHash('')).toBeNull()
    expect(parseAgentHash('#cpu')).toBeNull()
    expect(parseAgentHash(`#agent=0&token=${TOKEN}`)).toBeNull()
    expect(parseAgentHash(`#agent=70000&token=${TOKEN}`)).toBeNull()
    expect(parseAgentHash(`#agent=abc&token=${TOKEN}`)).toBeNull()
    expect(parseAgentHash('#agent=47615&token=short')).toBeNull()
    expect(parseAgentHash('#agent=47615&token=<script>alert(1)</script>aaaaaaaaaaaa')).toBeNull()
    expect(parseAgentHash('#agent=47615')).toBeNull()
  })
})

describe('AGENT_STEPS', () => {
  it('matches the agent collector ids', () => {
    expect([...AGENT_STEPS]).toEqual(['system', 'cpu', 'motherboard', 'memory', 'gpu', 'storage', 'display'])
  })
})
