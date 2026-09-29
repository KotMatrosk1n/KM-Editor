-- SPDX-License-Identifier: GPL-3.0-only
-- This module owns only the additional combat actors and rows it creates.
local enabled = {}
for label in string.gmatch("KM_TITAN_LABELS", "[^;]+") do enabled[label] = true end
local setups = setmetatable({}, { __mode = "k" })
local views = setmetatable({}, { __mode = "k" })
local actors = setmetatable({}, { __mode = "k" })
local serial = 0
local factory = c05424CF6
local setupClass = C2D7F486425487755.prototype
local viewClass = CA5A5606298DDCB29.prototype
local pokemonClass = CB12D9CA38DB7B2E1.prototype
local originalEvent = setupClass.F350C62D885D33A64
local originalStart = C6C53F5DDF74F5897.SC10246118071CC26
local originalCleanup = viewClass.F6672CF37857F5BE1
local originalDelete = pokemonClass.F3CC990DA6C6A7C4A

local function restore(state)
    if state.hidden then
        state.hidden = false
        pcall(function() state.model:f6754453E(state.visible) end)
    end
    if state.setup and state.setup[12] and state.actor and state.setup[12][0] == state.actor then
        state.setup[12][0] = state.story
    end
end

local function release(state)
    restore(state)
    if state.name then
        local name = state.name
        state.name = nil
        pcall(factory.fAE1D228E, name)
    end
    if state.actor then actors[state.actor] = nil end
    state.actor = nil
end

setupClass.F350C62D885D33A64 = function(self, label, side, flag, value)
    originalEvent(self, label, side, flag, value)
    if side ~= 1 or not enabled[label] then return end
    local fallback = self[5][1]
    local state = { fallback = fallback, label = label, setup = self }
    self[5][1] = c03C8030E.f101D811F()
    local ok = pcall(originalEvent, self, "km_titan_" .. label, side, flag, value)
    if ok then setups[self] = state else self[5][1] = fallback end
end

local function prepare(state)
    local setup = state.setup
    state.story = setup[12][0]
    if not state.story or not state.story:f9D8BC178() then return false end
    local storyScene = state.story:f462C9B70()
    if not storyScene or not storyScene:f9D8BC178() then return false end
    local scene = c682D8E4F.fEF94D11D("battle")
    if not scene or not scene:f9D8BC178() then return false end
    state.model = cECB91E31.fB41FD22F(state.story)
    if not cECB91E31.f04ACC3F2(state.model, nil) then return false end
    state.visible = state.model:f2CB8758F()
    local parameter = setup[5][1]:fFD034BC3(0)
    local x, y, z = state.story:f7360ED03()
    serial = serial + 1
    state.name = "km_titan_combat_" .. tostring(serial)
    factory.f3DD7B21F(scene, state.name, parameter, 0, x, y, z)
    for frame = 1, 600 do
        if not state.story:f9D8BC178() or not storyScene:f9D8BC178() or not scene:f9D8BC178() then return false end
        if factory.f7893328E(state.name) then
            local actor = factory.f6F41B608(state.name)
            if actor and actor:f9D8BC178() and actor:f462C9B70():f9D8BC178() then
                state.actor = actor
                local behavior = C3B091777E3EC94A5.S3AB27FFAF33EFD2D.h[actor:fB3CF1DEB()]
                if behavior and behavior.F97B7A02FD3401ACD then behavior:F97B7A02FD3401ACD() end
                state.hidden = true
                state.model:f6754453E(false)
                setup[12][0] = actor
                actors[actor] = state
                return true
            end
        end
        CC6FE82819C6E1D55.S12F63EE47FFCB183()
    end
    return false
end

C6C53F5DDF74F5897.SC10246118071CC26 = function(setup)
    local state = setups[setup]
    if not state then return originalStart(setup) end
    setups[setup] = nil
    local committed = false
    local guard <close> = setmetatable({}, { __close = function()
        if not committed then
            release(state)
            setup[5][1] = state.fallback
        end
    end })
    local ok, ready = false, false
    if setup[1] == 4 then ok, ready = pcall(prepare, state) end
    if not ok or not ready then
        release(state)
        setup[5][1] = state.fallback
        return originalStart(setup)
    end
    local view = originalStart(setup)
    if not view then setup[5][1] = state.fallback; return nil end
    views[view] = state
    committed = true
    return view
end

pokemonClass.F3CC990DA6C6A7C4A = function(self)
    local state = self[2] and actors[self[2]]
    if not state then return originalDelete(self) end
    self:F9E09A204E629F9F3(false)
    originalDelete(self)
    release(state)
    self[2] = nil
end

viewClass.F6672CF37857F5BE1 = function(self)
    local state = views[self]
    if not state then return originalCleanup(self) end
    local guard <close> = setmetatable({}, { __close = function()
        release(state)
        views[self] = nil
    end })
    return originalCleanup(self)
end
