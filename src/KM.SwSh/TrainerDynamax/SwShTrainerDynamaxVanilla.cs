// SPDX-License-Identifier: GPL-3.0-only

namespace KM.SwSh.TrainerDynamax;

internal static class SwShTrainerDynamaxVanilla
{
    // Original battle setup permissions for the supported executable versions.
    // Team flags, save progression and Pokemon eligibility are separate conditions.
    public static bool? Player(int trainerId) => trainerId switch
    {
        32 or 36 or 37 or 77 or 78 or 108 or 130 or 131 or 132 or 134 or 135 or 136 or 143 or 144 or 145 or 149 or
        175 or 189 or 190 or 194 or 210 or 211 or 212 or 213 or 249 or 250 or 251 or 252 or 253 or 254 or 255 or 256 or
        257 or 258 or 259 or 260 or 261 or 262 or 264 or 265 or 266 or 267 or 268 or 269 or 270 or 271 or 272 or 273 or
        274 or 275 or 276 or 277 or 278 or 279 or 280 or 281 or 282 or 283 or 284 or 289 or 319 or 320 or 324 or 325 or
        326 or 327 or 328 or 329 or 339 or 340 or 341 or 342 or 343 or 344 or 345 or 346 or 347 or 348 or 349 or 350 or
        351 or 352 or 353 or 354 or 355 or 356 or 357 or 358 or 359 or 360 or 361 or 362 or 363 or 364 or 365 or 372 or
        373 or 374 or 375 or 376 or 377 or 378 or 379 or 380 or 381 or 382 or 383 or 384 or 385 or 386 or 387 or 388 or
        389 or 390 or 391 or 392 or 393 or 394 or 395 or 396 or 397 or 398 or 399 or 400 or 401 or 402 or 403 or 404 or
        405 or 406 or 407 or 408 or 409 or 410 or 411 or 412 or 413 or 419 or 420 or 432 or 433 or 435 or 436 => true,
        1 or 11 or 58 or 71 or 90 or 91 or 95 or 109 or 110 or 111 or 112 or 116 or 117 or 118 or 119 or 120 or
        146 or 147 or 148 or 153 or 154 or 155 or 156 or 157 or 158 or 161 or 162 or 163 or 164 or 165 or 166 or 167 or
        168 or 169 or 170 or 171 or 172 or 173 or 174 or 188 or 197 or 198 or 199 or 200 or 209 or 217 or 218 or 219 or
        220 or 225 or 226 or 227 or 228 or 229 or 230 or 263 or 288 or 290 or 291 or 292 or 293 or 294 or 295 or 296 or
        297 or 298 or 299 or 300 or 301 or 302 or 303 or 307 or 308 or 312 or 313 or 314 or 366 or 367 or 368 or 369 or
        370 or 371 => null,
        >= 1 and <= 436 => false,
        _ => null,
    };

    public static bool? Opponent(int trainerId) => trainerId is 346 or 350 or 362 ? false : Player(trainerId);
}
